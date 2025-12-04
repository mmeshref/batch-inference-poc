using Shared;

namespace GpuWorker;

public interface IRequestRepository
{
    Task<RequestEntity?> DequeueAsync(string gpuPool, CancellationToken cancellationToken);
    Task<bool> MarkRunningAsync(RequestEntity request, string workerId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(RequestEntity request, CancellationToken cancellationToken);
    Task MarkFailedAsync(RequestEntity request, string errorMessage, CancellationToken cancellationToken);
}

