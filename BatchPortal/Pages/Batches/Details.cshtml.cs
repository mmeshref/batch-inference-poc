using BatchPortal.Models;
using BatchPortal.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared;

namespace BatchPortal.Pages.Batches;

public sealed class DetailsModel : PageModel
{
    private readonly BatchDbContext _dbContext;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(BatchDbContext dbContext, ILogger<DetailsModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public BatchDetailsViewModel? Batch { get; private set; }
    public IReadOnlyList<RequestViewModel> Requests { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool HasEscalatedRequests { get; private set; }
    public int EscalatedRequestCount { get; private set; }
    public DateTimeOffset? FirstEscalationAt { get; private set; }
    public string? StatusFilter { get; private set; }
    public IReadOnlyList<string> AvailableStatuses { get; } = new[]
    {
        RequestStatuses.Queued,
        RequestStatuses.Running,
        RequestStatuses.Completed,
        RequestStatuses.Failed
    };

    public async Task<IActionResult> OnGetAsync(Guid id, string? status)
    {
        var batchEntity = await _dbContext.Batches
            .Include(b => b.Requests)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (batchEntity is null)
        {
            ErrorMessage = $"Batch {id} was not found in the database.";
            _logger.LogWarning("Batch details requested for id {BatchId} but batch was not found in the database.", id);
            return Page();
        }

        StatusFilter = string.IsNullOrWhiteSpace(status) ? null : status;

        var requests = batchEntity.Requests.AsQueryable();
        if (!string.IsNullOrEmpty(StatusFilter))
        {
            requests = requests.Where(r => string.Equals(r.Status, StatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        Requests = requests
            .OrderBy(r => r.LineNumber)
            .Select(r => new RequestViewModel(
                r.Id,
                r.LineNumber,
                r.Status,
                r.GpuPool,
                r.InputPayload,
                r.OutputPayload,
                r.ErrorMessage,
                r.CreatedAt,
                r.StartedAt,
                r.CompletedAt))
            .ToList();

        var escalatedRequests = batchEntity.Requests
            .Where(r =>
                string.Equals(r.GpuPool, GpuPools.Dedicated, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(r.ErrorMessage) &&
                r.ErrorMessage.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase))
            .ToList();

        HasEscalatedRequests = escalatedRequests.Any();
        EscalatedRequestCount = escalatedRequests.Count;
        if (HasEscalatedRequests)
        {
            FirstEscalationAt = escalatedRequests
                .Select(r => r.StartedAt ?? r.CreatedAt)
                .OrderBy(dt => dt)
                .FirstOrDefault();
        }

        var slaDeadline = batchEntity.CreatedAt + batchEntity.CompletionWindow;
        var completedAt = batchEntity.CompletedAt;

        Batch = new BatchDetailsViewModel
        {
            Id = batchEntity.Id,
            UserId = batchEntity.UserId,
            Status = batchEntity.Status,
            GpuPool = batchEntity.GpuPool,
            CreatedAt = batchEntity.CreatedAt.UtcDateTime,
            StartedAt = batchEntity.StartedAt?.UtcDateTime,
            CompletedAt = completedAt?.UtcDateTime,
            CompletionWindow = batchEntity.CompletionWindow,
            SlaDeadline = slaDeadline.UtcDateTime,
            IsSlaBreached = completedAt.HasValue && completedAt.Value > slaDeadline,
            TotalRequests = batchEntity.Requests.Count,
            QueuedCount = batchEntity.Requests.Count(r => r.Status == RequestStatuses.Queued),
            RunningCount = batchEntity.Requests.Count(r => r.Status == RequestStatuses.Running),
            CompletedCount = batchEntity.Requests.Count(r => r.Status == RequestStatuses.Completed),
            FailedCount = batchEntity.Requests.Count(r => r.Status == RequestStatuses.Failed),
            Notes = batchEntity.ErrorMessage
        };

        return Page();
    }

public sealed record RequestViewModel(
    Guid Id,
    int LineNumber,
    string Status,
    string? GpuPool,
    string InputPayload,
    string? OutputPayload,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public bool WasRequeuedFromSpot =>
        !string.IsNullOrEmpty(ErrorMessage) &&
        ErrorMessage.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(GpuPool, GpuPools.Dedicated, StringComparison.OrdinalIgnoreCase);
}
}

