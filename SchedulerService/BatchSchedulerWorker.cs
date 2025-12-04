using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    private static readonly Counter RequestsDeduplicated = Metrics
        .CreateCounter("batch_scheduler_requests_deduplicated_total", "Requests that were deduplicated (skipped processing).");


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

    internal async Task ProcessSingleBatchAsync(BatchDbContext db, BatchEntity batch, CancellationToken cancellationToken)
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

        // Get deduplication service from DI
        var deduplicationService = _services.GetRequiredService<IDeduplicationService>();
        var deduplicatedCount = 0;

        await foreach (var line in ReadLinesAsync(file.StoragePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                lineNumber++;
                continue;
            }

            // Compute hash for deduplication
            var inputHash = deduplicationService.ComputeInputHash(line);
            
            // Check for duplicate
            var duplicate = await deduplicationService.FindDuplicateAsync(inputHash, batch.UserId, cancellationToken);

            var request = new RequestEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = lineNumber,
                InputPayload = line,
                OutputPayload = duplicate?.OutputPayload, // Copy output if duplicate found
                Status = duplicate != null ? RequestStatuses.Completed : RequestStatuses.Queued,
                GpuPool = initialPool,
                AssignedWorker = null,
                CreatedAt = DateTimeOffset.UtcNow,
                InputHash = inputHash,
                OriginalRequestId = duplicate?.Id,
                IsDeduplicated = duplicate != null
            };

            if (duplicate != null)
            {
                // Mark as completed immediately with copied output
                request.CompletedAt = DateTimeOffset.UtcNow;
                request.StartedAt = DateTimeOffset.UtcNow; // Set started time for consistency
                deduplicatedCount++;
                RequestsDeduplicated.Inc();
                
                _logger.LogDebug(
                    "Request {RequestId} deduplicated from original {OriginalRequestId}",
                    request.Id,
                    duplicate.Id);
            }

            await db.Requests.AddAsync(request, cancellationToken);
            lineNumber++;
        }

        if (deduplicatedCount > 0)
        {
            _logger.LogInformation(
                "Batch {BatchId}: {DeduplicatedCount} of {TotalCount} requests were deduplicated",
                batch.Id,
                deduplicatedCount,
                lineNumber);
        }

        batch.Status = "running";
        batch.StartedAt = DateTimeOffset.UtcNow;
        BatchesProcessed.Inc();

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Batch {BatchId} moved to running with {Count} requests", batch.Id, lineNumber);

        // Check if all requests are already completed (e.g., all deduplicated)
        // If so, finalize the batch immediately
        await TryFinalizeBatchIfAllCompletedAsync(db, batch.Id, cancellationToken);
    }

    private async Task EscalatePendingRequestsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;

        var pendingRequests = await db.Requests
            .Include(r => r.Batch)
            .Where(r => r.Status == RequestStatuses.Queued)
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

    internal async Task TryFinalizeBatchIfAllCompletedAsync(
        BatchDbContext db,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null || batch.OutputFileId.HasValue)
        {
            return;
        }

        // Check if there are any queued or running requests
        var remaining = await db.Requests
            .Where(r => r.BatchId == batchId && (r.Status == RequestStatuses.Queued || r.Status == RequestStatuses.Running))
            .CountAsync(cancellationToken);

        // If there are still pending requests, don't finalize yet
        if (remaining > 0)
        {
            return;
        }

        // All requests are completed - finalize the batch
        var failed = await db.Requests
            .Where(r => r.BatchId == batchId && r.Status == RequestStatuses.Failed)
            .CountAsync(cancellationToken);

        var basePath = _configuration["Storage:BasePath"] ?? "/tmp/dwb-files";
        Directory.CreateDirectory(basePath);

        var outputFileId = Guid.NewGuid();
        var outputFileName = $"output-{batchId}.jsonl";
        var outputPath = Path.Combine(basePath, outputFileName);

        var requests = await db.Requests
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

        await using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var r in requests)
            {
                var line = r.OutputPayload ?? JsonSerializer.Serialize(new
                {
                    error = r.ErrorMessage ?? "no output",
                    status = r.Status,
                    line_number = r.LineNumber
                });

                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }

        var fileEntity = new FileEntity
        {
            Id = outputFileId,
            UserId = batch.UserId,
            Filename = outputFileName,
            StoragePath = outputPath,
            Purpose = "batch-output",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await db.Files.AddAsync(fileEntity, cancellationToken);

        batch.OutputFileId = outputFileId;
        batch.CompletedAt = DateTimeOffset.UtcNow;
        batch.Status = failed > 0 ? "failed" : "completed";
        batch.ErrorMessage = failed > 0 ? "One or more requests failed" : null;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Finalized batch {BatchId} with status {Status} (all requests completed immediately, e.g., via deduplication). Output file {OutputFileId}",
            batchId,
            batch.Status,
            outputFileId);
    }
}

