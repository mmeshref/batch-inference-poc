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

        // Claim a pending request for this gpu_pool
        var request = await db.Requests
            .Where(r => r.Status == "pending" && r.GpuPool == _gpuPool)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return false;
        }

        // Mark as running
        request.Status = "running";
        request.AssignedWorker = _workerId;
        request.StartedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        // Simulate “GPU” processing
        await SimulateInferenceAsync(request, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        // After processing, check if batch is complete
        await TryFinalizeBatchAsync(db, request.BatchEntityId, cancellationToken);

        return true;
    }

    private async Task SimulateInferenceAsync(RequestEntity request, CancellationToken cancellationToken)
    {
        // Dedicated: low latency, low failure
        // Spot: higher latency, some failures
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
            request.Status = "completed";
            request.CompletedAt = DateTimeOffset.UtcNow;
            RequestsCompleted.WithLabels(_gpuPool).Inc();
        }
        else
        {
            var delayMs = _random.Next(100, 2000);
            await Task.Delay(delayMs, cancellationToken);

            var fail = _random.NextDouble() < _spotFailureRate;

            if (fail)
            {
                request.Status = "failed";
                request.CompletedAt = DateTimeOffset.UtcNow;
                request.ErrorMessage = "Simulated spot interruption";
                RequestsFailed.WithLabels(_gpuPool).Inc();
            }
            else
            {
                var output = new
                {
                    input = request.InputPayload,
                    processed_by = _workerId,
                    gpu_pool = _gpuPool,
                    latency_ms = delayMs
                };

                request.OutputPayload = JsonSerializer.Serialize(output);
                request.Status = "completed";
                request.CompletedAt = DateTimeOffset.UtcNow;
                RequestsCompleted.WithLabels(_gpuPool).Inc();
            }
        }
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
            .Where(r => r.BatchEntityId == batchId)
            .CountAsync(cancellationToken);

        var remaining = await db.Requests
            .Where(r => r.BatchEntityId == batchId && (r.Status == "pending" || r.Status == "running"))
            .CountAsync(cancellationToken);

        if (remaining > 0)
        {
            return;
        }

        var failed = await db.Requests
            .Where(r => r.BatchEntityId == batchId && r.Status == "failed")
            .CountAsync(cancellationToken);

        // All done (completed or failed). Generate output file.
        var basePath = _configuration["Storage:BasePath"] ?? "/tmp/dwb-files";
        Directory.CreateDirectory(basePath);

        var outputFileId = Guid.NewGuid();
        var outputFileName = $"output-{batchId}.jsonl";
        var outputPath = Path.Combine(basePath, outputFileName);

        var requests = await db.Requests
            .Where(r => r.BatchEntityId == batchId)
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
            "Finalized batch {BatchId} with status {Status}. Output file {OutputFileId}",
            batchId,
            batch.Status,
            outputFileId);
    }
}
