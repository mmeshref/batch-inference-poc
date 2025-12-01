using Prometheus;

namespace ApiGateway;

public static class BatchMetrics
{
    // Total batches that have moved out of "queued" state at least once.
    public static readonly Gauge BatchesProcessedTotal = Metrics.CreateGauge(
        "batch_scheduler_batches_processed_total",
        "Total number of batches that have ever been started (status running/completed/failed).");

    // Total requests completed successfully (across all pools)
    public static readonly Gauge GpuWorkerRequestsCompletedTotal = Metrics.CreateGauge(
        "gpu_worker_requests_completed_total",
        "Total number of requests completed successfully across all GPU pools.");

    // Total requests failed (across all pools)
    public static readonly Gauge GpuWorkerRequestsFailedTotal = Metrics.CreateGauge(
        "gpu_worker_requests_failed_total",
        "Total number of requests that ended in failed state across all GPU pools.");
}
