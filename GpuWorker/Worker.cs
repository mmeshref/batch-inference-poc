using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using GpuWorker;
using GpuWorker.Models;

public sealed class GpuWorkerService : BackgroundService
{
    private readonly ILogger<GpuWorkerService> _logger;
    private readonly ILogger<WorkerPullLoop> _pullLoopLogger;
    private readonly IConfiguration _configuration;
    private readonly IRequestRepository _requestRepository;
    private readonly string _gpuPool;
    private readonly string _workerId;
    private readonly double _spotFailureRate;
    private readonly Random _random = new();


    public GpuWorkerService(
        ILogger<GpuWorkerService> logger,
        ILogger<WorkerPullLoop> pullLoopLogger,
        IConfiguration configuration,
        IRequestRepository requestRepository)
    {
        _logger = logger;
        _pullLoopLogger = pullLoopLogger;
        _configuration = configuration;
        _requestRepository = requestRepository;

        _gpuPool = Environment.GetEnvironmentVariable("GPU_POOL") ?? "spot";
        _workerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? Environment.MachineName;

        _spotFailureRate = _configuration.GetValue<double?>("Worker:SpotFailureRate") ?? 0.1;

        _logger.LogInformation("GpuWorkerService starting with GPU_POOL={Pool}, WORKER_ID={WorkerId}", _gpuPool, _workerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GpuWorkerService loop started");

        var pullLoop = new WorkerPullLoop(_pullLoopLogger, _requestRepository, _gpuPool, _workerId);

        await pullLoop.RunAsync(
            async (request, token) => await ProcessRequestAsync(request, token),
            stoppingToken);

        _logger.LogInformation("GpuWorkerService stopping");
    }
    

    private async Task ProcessRequestAsync(RequestEntity request, CancellationToken cancellationToken)
        {
            await SimulateInferenceAsync(request, cancellationToken);
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
        }
    }

}
