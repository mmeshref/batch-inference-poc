using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;

namespace ApiGateway;

public sealed class BatchMetricsUpdater : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BatchMetricsUpdater> _logger;
    private readonly TimeSpan _interval;

    public BatchMetricsUpdater(IServiceProvider services, ILogger<BatchMetricsUpdater> logger)
    {
        _services = services;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(10); // update every 10s

        _logger.LogInformation("BatchMetricsUpdater constructed");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BatchMetricsUpdater started with interval {Seconds}s", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateMetricsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating batch metrics");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("BatchMetricsUpdater stopping");
    }

    private async Task UpdateMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

        // batches processed = any batch not in 'queued'
        var batchesProcessed = await db.Batches
            .Where(b => b.Status != "queued")
            .CountAsync(cancellationToken);

        var requestsCompleted = await db.Requests
            .Where(r => r.Status == RequestStatuses.Completed)
            .CountAsync(cancellationToken);

        var requestsFailed = await db.Requests
            .Where(r => r.Status == RequestStatuses.Failed)
            .CountAsync(cancellationToken);

        BatchMetrics.BatchesProcessedTotal.Set(batchesProcessed);
        BatchMetrics.GpuWorkerRequestsCompletedTotal.Set(requestsCompleted);
        BatchMetrics.GpuWorkerRequestsFailedTotal.Set(requestsFailed);

        _logger.LogDebug(
            "Updated metrics: batchesProcessed={Batches}, completed={Completed}, failed={Failed}",
            batchesProcessed, requestsCompleted, requestsFailed);
    }
}
