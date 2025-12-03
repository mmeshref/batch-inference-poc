using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using Prometheus;
using GpuWorker.Models;
using GpuWorker;

public sealed class GpuWorkerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<GpuWorkerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _gpuPool;
    private readonly string _workerId;
    private readonly TimeSpan _pollInterval;
    private readonly double _spotFailureRate;
    private readonly Random _random = new();
    
    private static readonly Counter RequestsCompleted = Metrics
    .CreateCounter("gpu_worker_requests_completed_total", "Requests completed successfully.", new[] { "gpu_pool" });

    private static readonly Counter RequestsFailed = Metrics
    .CreateCounter("gpu_worker_requests_failed_total", "Requests failed.", new[] { "gpu_pool" });


    public GpuWorkerService(
        IServiceProvider services,
        ILogger<GpuWorkerService> logger,
        IConfiguration configuration)
    {
        _services = services;
        _logger = logger;
        _configuration = configuration;

        _gpuPool = Environment.GetEnvironmentVariable("GPU_POOL") ?? "spot";
        _workerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? Environment.MachineName;

        var pollSeconds = _configuration.GetValue<int?>("Worker:PollIntervalSeconds") ?? 1;
        _pollInterval = TimeSpan.FromSeconds(pollSeconds);

        _spotFailureRate = _configuration.GetValue<double?>("Worker:SpotFailureRate") ?? 0.1;

        _logger.LogInformation("GpuWorkerService starting with GPU_POOL={Pool}, WORKER_ID={WorkerId}", _gpuPool, _workerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GpuWorkerService loop started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var didWork = await ProcessOneRequestAsync(stoppingToken);

                if (!didWork)
                {
                    // Nothing to do, back off a bit
                    await Task.Delay(_pollInterval, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing request");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("GpuWorkerService stopping");
    }
    

    private async Task<bool> ProcessOneRequestAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

        var request = await TryDequeueNextRequestAsync(db, _gpuPool, cancellationToken);

        if (request is null)
        {
            return false;
        }

        try
        {
            await SimulateInferenceAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            var requeued = await TryHandleSpotInterruptionAsync(db, request, ex, cancellationToken);
            if (!requeued)
            {
                throw;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.Status != RequestStatus.Queued)
        {
            await TryFinalizeBatchAsync(db, request.BatchId, cancellationToken);
        }

        return true;
    }

    private async Task<RequestEntity?> TryDequeueNextRequestAsync(
        BatchDbContext db,
        string gpuPool,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        const string queuedStatus = "Queued";

        var next = await db.Requests
            .FromSqlRaw(@"
                SELECT *
                FROM requests
                WHERE ""Status"" = {0}
                  AND ""GpuPool"" = {1}
                ORDER BY ""CreatedAt""
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            ", queuedStatus, gpuPool)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        next.Status = RequestStatus.Running;
        next.AssignedWorker = _workerId;
        next.StartedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return next;
    }

    private async Task SimulateInferenceAsync(RequestEntity request, CancellationToken cancellationToken)
    {
        RequestPayload? payload = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.InputPayload))
            {
                payload = JsonSerializer.Deserialize<RequestPayload>(request.InputPayload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize request payload for request {RequestId}", request.Id);
        }

        if (payload?.SleepSeconds is int sleepSeconds && sleepSeconds > 0)
        {
            const int maxSeconds = 600;
            var clamped = Math.Min(sleepSeconds, maxSeconds);
            _logger.LogInformation(
                "Simulating slow processing for request {RequestId} with sleep_seconds={SleepSeconds}",
                request.Id,
                clamped);
            await Task.Delay(TimeSpan.FromSeconds(clamped), cancellationToken);
        }

        if (string.Equals(_gpuPool, "dedicated", StringComparison.OrdinalIgnoreCase))
        {
            var delayMs = _random.Next(100, 500);
            await Task.Delay(delayMs, cancellationToken);

            var output = new
            {
                input = request.InputPayload,
                processed_by = _workerId,
                gpu_pool = _gpuPool,
                latency_ms = delayMs
            };

            request.OutputPayload = JsonSerializer.Serialize(output);
            RequestStateTransition.MarkCompleted(request, DateTimeOffset.UtcNow);
            RequestsCompleted.WithLabels(_gpuPool).Inc();
        }
        else
        {
            var delayMs = _random.Next(100, 2000);
            await Task.Delay(delayMs, cancellationToken);

            var fail = _random.NextDouble() < _spotFailureRate;

            if (fail)
            {
                throw new InvalidOperationException("Simulated spot interruption");
            }

            var output = new
            {
                input = request.InputPayload,
                processed_by = _workerId,
                gpu_pool = _gpuPool,
                latency_ms = delayMs
            };

            request.OutputPayload = JsonSerializer.Serialize(output);
            RequestStateTransition.MarkCompleted(request, DateTimeOffset.UtcNow);
            RequestsCompleted.WithLabels(_gpuPool).Inc();
        }
    }

    private async Task<bool> TryHandleSpotInterruptionAsync(
        BatchDbContext db,
        RequestEntity request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isSpot = string.Equals(request.GpuPool, "spot", StringComparison.OrdinalIgnoreCase);
        var isInterruption = exception.Message?.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase) == true;

        if (!isSpot || !isInterruption)
        {
            var message = exception.Message ?? "Unknown error";
            RequestStateTransition.MarkTerminalFailure(request, DateTimeOffset.UtcNow, message);
            RequestsFailed.WithLabels(_gpuPool).Inc();
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation(
            "Request {RequestId} for batch {BatchId} was interrupted on spot and requeued for dedicated processing.",
            request.Id,
            request.BatchId);

        RequestStateTransition.MarkTransientFailureRequeued(request, exception.Message);
        request.GpuPool = "dedicated";

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task TryFinalizeBatchAsync(
        BatchDbContext db,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        // Re-load batch + request counts
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null)
        {
            return;
        }

        // If batch already has output file, someone else finalized it
        if (batch.OutputFileId.HasValue)
        {
            return;
        }

        var total = await db.Requests
            .Where(r => r.BatchId == batchId)
            .CountAsync(cancellationToken);

        var remaining = await db.Requests
            .Where(r => r.BatchId == batchId && (r.Status == RequestStatus.Queued || r.Status == RequestStatus.Running))
            .CountAsync(cancellationToken);

        if (remaining > 0)
        {
            return;
        }

        var failed = await db.Requests
            .Where(r => r.BatchId == batchId && r.Status == RequestStatus.Failed)
            .CountAsync(cancellationToken);

        // All done (completed or failed). Generate output file.
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
                // If no output payload (failed), emit something explicit
                var line = r.OutputPayload ?? JsonSerializer.Serialize(new
                {
                    error = r.ErrorMessage ?? "no output",
                    status = r.Status.ToString(),
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
            "Finalized batch {BatchId} with status {Status}. Output file {OutputFileId}",
            batchId,
            batch.Status,
            outputFileId);
    }
}
