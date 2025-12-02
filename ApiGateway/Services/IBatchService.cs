using Shared;

namespace ApiGateway.Services;

public interface IBatchService
{
    Task<BatchEntity> CreateBatchAsync(
        string userId,
        Guid inputFileId,
        string gpuPool,
        string? userName,
        TimeSpan completionWindow,
        int priority,
        CancellationToken cancellationToken);
}

