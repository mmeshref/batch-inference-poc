using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;

namespace GpuWorker;

public sealed class RequestRepository : IRequestRepository
{
    private readonly IDbContextFactory<BatchDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RequestRepository> _logger;

    public RequestRepository(
        IDbContextFactory<BatchDbContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<RequestRepository> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RequestEntity?> DequeueAsync(string gpuPool, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var providerName = db.Database.ProviderName ?? string.Empty;
        var supportsSkipLocked = providerName.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0;

        if (supportsSkipLocked)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var nextRow = await db.Requests
                .FromSqlRaw(@"
                    SELECT r.*
                    FROM requests r
                    INNER JOIN batches b ON r.""BatchId"" = b.""Id""
                    WHERE r.""Status"" = {0}
                      AND r.""GpuPool"" = {1}
                    ORDER BY b.""Priority"" DESC, r.""CreatedAt"" ASC
                    FOR UPDATE OF r SKIP LOCKED
                    LIMIT 1
                ", RequestStatuses.Queued, gpuPool)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (nextRow is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await transaction.CommitAsync(cancellationToken);
            return nextRow;
        }

        var fallback = await db.Requests
            .Include(r => r.Batch)
            .Where(r => r.Status == RequestStatuses.Queued && r.GpuPool == gpuPool)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return fallback
            .OrderByDescending(r => r.Batch?.Priority ?? 0)
            .ThenBy(r => r.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<bool> MarkRunningAsync(RequestEntity request, string workerId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var affected = await db.Requests
            .Where(r => r.Id == request.Id && r.Status == RequestStatuses.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Status, RequestStatuses.Running)
                    .SetProperty(r => r.AssignedWorker, workerId)
                    .SetProperty(r => r.StartedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (affected > 0)
        {
            request.Status = RequestStatuses.Running;
            request.AssignedWorker = workerId;
            request.StartedAt = DateTimeOffset.UtcNow;
            return true;
        }

        return false;
    }

    public async Task MarkCompletedAsync(RequestEntity request, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Requests.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var completedAt = request.CompletedAt ?? DateTimeOffset.UtcNow;

        entity.Status = RequestStatuses.Completed;
        entity.OutputPayload = request.OutputPayload;
        entity.CompletedAt = completedAt;
        entity.ErrorMessage = null;

        request.Status = RequestStatuses.Completed;
        request.CompletedAt = completedAt;

        await db.SaveChangesAsync(cancellationToken);

        await TryFinalizeBatchAsync(db, entity.BatchId, cancellationToken);
    }

    public async Task MarkFailedAsync(RequestEntity request, string errorMessage, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Requests.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var isSpotInterruption =
            string.Equals(entity.GpuPool, GpuPools.Spot, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(errorMessage) &&
            errorMessage.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase);

        if (isSpotInterruption)
        {
            entity.Status = RequestStatuses.Queued;
            entity.GpuPool = GpuPools.Dedicated;
            entity.AssignedWorker = null;
            entity.StartedAt = null;
            entity.CompletedAt = null;
            entity.ErrorMessage = errorMessage;

            request.Status = RequestStatuses.Queued;
            request.GpuPool = GpuPools.Dedicated;
            request.AssignedWorker = null;
            request.StartedAt = null;
            request.CompletedAt = null;
            request.ErrorMessage = errorMessage;

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        entity.Status = RequestStatuses.Failed;
        entity.ErrorMessage = errorMessage;
        entity.CompletedAt = DateTimeOffset.UtcNow;

        request.Status = RequestStatuses.Failed;
        request.ErrorMessage = errorMessage;
        request.CompletedAt = entity.CompletedAt;

        await db.SaveChangesAsync(cancellationToken);

        await TryFinalizeBatchAsync(db, entity.BatchId, cancellationToken);
    }

    private async Task TryFinalizeBatchAsync(
        BatchDbContext db,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null || batch.OutputFileId.HasValue)
        {
            return;
        }

        var remaining = await db.Requests
            .Where(r => r.BatchId == batchId && (r.Status == RequestStatuses.Queued || r.Status == RequestStatuses.Running))
            .CountAsync(cancellationToken);

        if (remaining > 0)
        {
            return;
        }

        var failed = await db.Requests
            .Where(r => r.BatchId == batchId && r.Status == RequestStatuses.Failed)
            .CountAsync(cancellationToken);

        var basePath = _configuration["Storage:BasePath"] ?? "/tmp/dwb-files";
        Directory.CreateDirectory(basePath);

        var outputFileId = Guid.NewGuid();
        var outputFileName = $"output-{batchId}.jsonl";
        var outputPath = Path.Combine(basePath, outputFileName);

        var requests = await db.Requests
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

        await using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var r in requests)
            {
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

