using System.Collections.Generic;
using System.IO;
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
                BatchEntityId = batch.Id,
                LineNumber = lineNumber,
                InputPayload = line,
                OutputPayload = null,
                Status = "pending",
                GpuPool = batch.GpuPool,
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

