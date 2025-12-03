using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using Prometheus;

namespace SchedulerService;

public sealed class BatchSchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BatchSchedulerWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _slaEscalationThreshold;

    private static readonly Counter BatchesProcessed = Metrics
    .CreateCounter("batch_scheduler_batches_processed_total", "Batches that have been moved from queued to running.");

    private static readonly Counter BatchesFailed = Metrics
    .CreateCounter("batch_scheduler_batches_failed_total", "Batches that failed in scheduler.");


    public BatchSchedulerWorker(
        IServiceProvider services,
        ILogger<BatchSchedulerWorker> logger,
        IConfiguration configuration)
    {
        _services = services;
        _logger = logger;
        _configuration = configuration;

        var thresholdHours = _configuration.GetValue<double?>("Scheduling:SlaEscalationThresholdHours") ?? 2d;
        if (thresholdHours <= 0)
        {
            thresholdHours = 2d;
        }

        _slaEscalationThreshold = TimeSpan.FromHours(thresholdHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _configuration.GetValue<int?>("Scheduler:IntervalSeconds") ?? 5;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation("BatchSchedulerWorker started with interval {IntervalSeconds}s", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedBatchesAsync(stoppingToken);
                await EscalatePendingRequestsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing queued batches");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ProcessQueuedBatchesAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

        var queuedBatches = await db.Batches
            .Where(b => b.Status == "queued")
            .OrderBy(b => b.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (queuedBatches.Count == 0)
        {
            return;
        }

        foreach (var batch in queuedBatches)
        {
            await ProcessSingleBatchAsync(db, batch, cancellationToken);
        }
    }

    private async Task ProcessSingleBatchAsync(BatchDbContext db, BatchEntity batch, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing queued batch {BatchId}", batch.Id);

        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == batch.InputFileId && f.UserId == batch.UserId, cancellationToken);

        if (file is null)
        {
            _logger.LogWarning("Input file {FileId} for batch {BatchId} not found", batch.InputFileId, batch.Id);
            batch.Status = "failed";
            batch.ErrorMessage = "Input file not found";
            BatchesFailed.Inc();
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!File.Exists(file.StoragePath))
        {
            _logger.LogWarning("Input file path {Path} for batch {BatchId} does not exist", file.StoragePath, batch.Id);
            batch.Status = "failed";
            batch.ErrorMessage = "Input file path does not exist";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var lineNumber = 0;
        var nowUtc = DateTimeOffset.UtcNow;
        var initialPool = SchedulingLogic.DetermineGpuPool(
            batch.CreatedAt,
            batch.CompletionWindow,
            nowUtc,
            _slaEscalationThreshold,
            batch.GpuPool);

        if (initialPool == "dedicated" && !string.Equals(batch.GpuPool, "dedicated", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Batch {BatchId} escalated to dedicated pool at ingestion. TimeUntilDeadline={TimeToDeadline}",
                batch.Id,
                (batch.CreatedAt + batch.CompletionWindow) - nowUtc);
            batch.GpuPool = "dedicated";
        }

        await foreach (var line in ReadLinesAsync(file.StoragePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                lineNumber++;
                continue;
            }

            var request = new RequestEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = lineNumber,
                InputPayload = line,
                OutputPayload = null,
                Status = RequestStatus.Queued,
                GpuPool = initialPool,
                AssignedWorker = null,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await db.Requests.AddAsync(request, cancellationToken);
            lineNumber++;
        }

        batch.Status = "running";
        batch.StartedAt = DateTimeOffset.UtcNow;
        BatchesProcessed.Inc();

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Batch {BatchId} moved to running with {Count} requests", batch.Id, lineNumber);
    }

    private async Task EscalatePendingRequestsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;

        var pendingRequests = await db.Requests
            .Include(r => r.Batch)
            .Where(r => r.Status == RequestStatus.Queued)
            .ToListAsync(cancellationToken);

        if (pendingRequests.Count == 0)
        {
            return;
        }

        var updated = false;
        var escalationsByBatch = new Dictionary<Guid, int>();

        foreach (var request in pendingRequests)
        {
            if (request.Batch is null)
            {
                _logger.LogWarning("Request {RequestId} missing batch reference; skipping SLA evaluation.", request.Id);
                continue;
            }

            var batch = request.Batch;

            var currentPool = request.GpuPool;
            var pool = currentPool;

            pool = SchedulingLogic.DetermineGpuPool(
                batch.CreatedAt,
                batch.CompletionWindow,
                nowUtc,
                _slaEscalationThreshold,
                currentPool);

            if (pool == "dedicated" && !string.Equals(currentPool, "dedicated", StringComparison.OrdinalIgnoreCase))
            {
                var timeToDeadline = (batch.CreatedAt + batch.CompletionWindow) - nowUtc;
                _logger.LogInformation(
                    "Escalating request {RequestId} of batch {BatchId} to dedicated pool. TimeUntilDeadline={TimeToDeadline}",
                    request.Id,
                    batch.Id,
                    timeToDeadline);

                request.GpuPool = "dedicated";
                batch.GpuPool = "dedicated";
                updated = true;
                escalationsByBatch[batch.Id] = escalationsByBatch.GetValueOrDefault(batch.Id) + 1;
            }

            if (string.Equals(request.GpuPool, "dedicated", StringComparison.OrdinalIgnoreCase) &&
                request.ErrorMessage?.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogInformation(
                    "Scheduler is dispatching requeued request {RequestId} (after spot interruption) to dedicated pool.",
                    request.Id);
            }
        }

        if (updated)
        {
            await db.SaveChangesAsync(cancellationToken);

            foreach (var kvp in escalationsByBatch)
            {
                var batch = pendingRequests.FirstOrDefault(r => r.Batch?.Id == kvp.Key)?.Batch;
                if (batch is null)
                {
                    continue;
                }
                var deadline = batch.CreatedAt + batch.CompletionWindow;
                var remainingPending = pendingRequests.Count(r => r.Batch?.Id == kvp.Key && !string.Equals(r.GpuPool, "dedicated", StringComparison.OrdinalIgnoreCase));

                _logger.LogInformation(
                    "Batch {BatchId} escalated to dedicated pool. TimeUntilDeadline={TimeToDeadline}, RequestsEscalated={RequestCount}, RemainingSpotRequests={RemainingSpot}",
                    kvp.Key,
                    deadline - nowUtc,
                    kvp.Value,
                    remainingPending);
            }
        }
    }

    private static string DetermineGpuPool(BatchEntity batch, DateTimeOffset nowUtc, TimeSpan slaEscalationThreshold)
    {
        return SchedulingLogic.DetermineGpuPool(
            batch.CreatedAt,
            batch.CompletionWindow,
            nowUtc,
            slaEscalationThreshold,
            batch.GpuPool);
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }
}

