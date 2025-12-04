using Prometheus;

namespace GpuWorker;

public static class WorkerMetrics
{
    public static readonly Counter WorkerDequeueTotal = Metrics.CreateCounter(
        "worker_dequeue_total",
        "Total jobs dequeued by the worker.",
        new[] { "gpu_pool" });

    public static readonly Counter WorkerCompletedTotal = Metrics.CreateCounter(
        "worker_completed_total",
        "Total jobs completed successfully.",
        new[] { "gpu_pool" });

    public static readonly Counter WorkerFailedTotal = Metrics.CreateCounter(
        "worker_failed_total",
        "Total jobs that failed.",
        new[] { "gpu_pool" });

    public static readonly Counter WorkerIdleSeconds = Metrics.CreateCounter(
        "worker_idle_seconds",
        "Total seconds spent idling while waiting for work.",
        new[] { "gpu_pool" });
}

