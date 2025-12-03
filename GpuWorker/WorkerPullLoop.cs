using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shared;

namespace GpuWorker;

public sealed class WorkerPullLoop
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly ILogger<WorkerPullLoop> _logger;
    private readonly IRequestRepository _repository;
    private readonly string _gpuPool;
    private readonly string _workerId;
    private readonly BackoffStrategy _backoffStrategy;

    public WorkerPullLoop(
        ILogger<WorkerPullLoop> logger,
        IRequestRepository repository,
        string gpuPool,
        string workerId,
        BackoffStrategy? backoffStrategy = null)
    {
        _logger = logger;
        _repository = repository;
        _gpuPool = gpuPool;
        _workerId = workerId;
        _backoffStrategy = backoffStrategy ?? new BackoffStrategy();
    }

    public async Task RunAsync(
        Func<RequestEntity, CancellationToken, Task> processRequestAsync,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RequestEntity? request = null;

            try
            {
                request = await _repository.DequeueAsync(_gpuPool, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dequeue request from pool {Pool}", _gpuPool);
            }

            if (request is null)
            {
                var delay = _backoffStrategy.NextDelay();
                _logger.LogDebug("Worker idle — backing off {Delay}", delay);
                WorkerMetrics.WorkerIdleSeconds.WithLabels(_gpuPool).Inc(delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                continue;
            }

            var markedRunning = await _repository.MarkRunningAsync(request, _workerId, stoppingToken);
            if (!markedRunning)
            {
                continue;
            }

            WorkerMetrics.WorkerDequeueTotal.WithLabels(_gpuPool).Inc();
            _backoffStrategy.Reset();
            _logger.LogDebug("Dequeued job {RequestId}", request.Id);
            _logger.LogDebug("Processing job {RequestId}", request.Id);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await processRequestAsync(request, stoppingToken);
                stopwatch.Stop();

                await _repository.MarkCompletedAsync(request, stoppingToken);
                WorkerMetrics.WorkerCompletedTotal.WithLabels(_gpuPool).Inc();

                _logger.LogDebug("Completed job {RequestId} in {Duration}", request.Id, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                WorkerMetrics.WorkerFailedTotal.WithLabels(_gpuPool).Inc();
                await _repository.MarkFailedAsync(request, ex.Message ?? "Unknown error", stoppingToken);
                var requeued = request.Status == RequestStatuses.Queued;

                if (!requeued)
                {
                    _logger.LogError(ex, "Failed job {RequestId} after {Duration}", request.Id, stopwatch.Elapsed);
                }
                else
                {
                    _logger.LogWarning(ex, "Job {RequestId} requeued to dedicated after interruption", request.Id);
                }
            }
        }
    }
}

