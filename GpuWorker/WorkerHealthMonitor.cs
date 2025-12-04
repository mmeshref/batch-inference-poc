using System.Threading;

namespace GpuWorker;

public interface IWorkerHealthMonitor
{
    bool IsReady { get; }
    void MarkReady();
}

public sealed class WorkerHealthMonitor : IWorkerHealthMonitor
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public void MarkReady()
    {
        Interlocked.Exchange(ref _ready, 1);
    }
}

