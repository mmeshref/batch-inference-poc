using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared;

namespace ApiGateway.Services;

public sealed class BatchService : IBatchService
{
    private readonly BatchDbContext _dbContext;
    private readonly ILogger<BatchService> _logger;

    public BatchService(BatchDbContext dbContext, ILogger<BatchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<BatchEntity> CreateBatchAsync(
        string userId,
        Guid inputFileId,
        string gpuPool,
        string? userName,
        TimeSpan completionWindow,
        int priority,
        CancellationToken cancellationToken)
    {
        var inputFile = await _dbContext.Files
            .FirstOrDefaultAsync(f => f.Id == inputFileId && f.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("Input file not found for user.");

        var batch = new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InputFileId = inputFile.Id,
            OutputFileId = null,
            Status = "queued",
            Endpoint = "mock-endpoint",
            CompletionWindow = completionWindow,
            Priority = priority,
            GpuPool = gpuPool,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Batches.Add(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created batch {BatchId} for user {UserId} targeting GPU pool {GpuPool}", batch.Id, userId, gpuPool);
        return batch;
    }
}

