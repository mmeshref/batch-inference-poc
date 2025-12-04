using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;

namespace SchedulerService;

public interface IDeduplicationService
{
    string ComputeInputHash(string inputPayload);
    Task<RequestEntity?> FindDuplicateAsync(string inputHash, string? userId, CancellationToken cancellationToken);
}

public sealed class DeduplicationService : IDeduplicationService
{
    private readonly IDbContextFactory<BatchDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeduplicationService> _logger;
    private readonly bool _enabled;
    private readonly bool _perUserScope;

    public DeduplicationService(
        IDbContextFactory<BatchDbContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<DeduplicationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _logger = logger;
        
        _enabled = _configuration.GetValue<bool>("Deduplication:Enabled", defaultValue: true);
        _perUserScope = _configuration.GetValue<bool>("Deduplication:PerUserScope", defaultValue: false);
    }

    public string ComputeInputHash(string inputPayload)
    {
        // Normalize JSON input to handle whitespace differences
        string normalizedInput;
        try
        {
            // Parse and re-serialize to normalize JSON formatting
            var jsonDoc = JsonDocument.Parse(inputPayload);
            normalizedInput = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
        }
        catch
        {
            // If not valid JSON, use as-is
            normalizedInput = inputPayload;
        }

        // Compute SHA256 hash
        var bytes = Encoding.UTF8.GetBytes(normalizedInput);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<RequestEntity?> FindDuplicateAsync(string inputHash, string? userId, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return null;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Requests
            .Include(r => r.Batch)
            .Where(r => r.InputHash == inputHash && 
                        r.Status == RequestStatuses.Completed &&
                        r.OutputPayload != null);

        // If per-user scope is enabled, only look for duplicates from the same user
        if (_perUserScope && !string.IsNullOrEmpty(userId))
        {
            query = query.Where(r => r.Batch != null && r.Batch.UserId == userId);
        }

        // Note: SQLite doesn't support DateTimeOffset in ORDER BY, so we need to materialize and sort in memory
        // For production with PostgreSQL, this could be optimized to sort in the database
        var candidates = await query
            .ToListAsync(cancellationToken);

        var duplicate = candidates
            .OrderByDescending(r => r.CompletedAt) // Get the most recent completed duplicate
            .FirstOrDefault();

        if (duplicate != null)
        {
            _logger.LogDebug(
                "Found duplicate request for hash {Hash}. Original request: {OriginalRequestId}",
                inputHash,
                duplicate.Id);
        }

        return duplicate;
    }
}

