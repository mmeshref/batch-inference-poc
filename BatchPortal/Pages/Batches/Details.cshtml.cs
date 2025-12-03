using BatchPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BatchPortal.Pages.Batches;

public sealed class DetailsModel : PageModel
{
    private readonly BatchDbContext _dbContext;

    public DetailsModel(BatchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public BatchDetailsViewModel Batch { get; private set; } = default!;
    public IReadOnlyList<RequestViewModel> Requests { get; private set; } = Array.Empty<RequestViewModel>();
    public string? StatusFilter { get; private set; }
    public bool HasEscalatedRequests { get; private set; }
    public int EscalatedRequestCount { get; private set; }
    public DateTimeOffset? FirstEscalationAt { get; private set; }
    public IReadOnlyList<string> AvailableStatuses { get; } = new[]
    {
        RequestStatuses.Queued,
        RequestStatuses.Running,
        RequestStatuses.Completed,
        RequestStatuses.Failed
    };

    public async Task<IActionResult> OnGetAsync(Guid id, string? status, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.Batches
            .Include(b => b.Requests)
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        Batch = MapToViewModel(batch);

        StatusFilter = string.IsNullOrWhiteSpace(status) ? null : status;

        var query = batch.Requests.AsQueryable();
        if (!string.IsNullOrWhiteSpace(StatusFilter))
        {
            query = query.Where(r => string.Equals(r.Status, StatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        Requests = query
            .OrderBy(r => r.CreatedAt)
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

        var escalatedRequests = batch.Requests
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

        return Page();
    }

    internal static BatchDetailsViewModel MapToViewModel(BatchEntity batch)
    {
        var deadline = batch.CreatedAt + batch.CompletionWindow;
        var requests = batch.Requests ?? Array.Empty<RequestEntity>();

        return new BatchDetailsViewModel
        {
            Id = batch.Id,
            UserId = batch.UserId,
            Status = batch.Status,
            GpuPool = batch.GpuPool,
            CreatedAt = batch.CreatedAt.UtcDateTime,
            StartedAt = batch.StartedAt?.UtcDateTime,
            CompletedAt = batch.CompletedAt?.UtcDateTime,
            CompletionWindow = batch.CompletionWindow,
            SlaDeadline = deadline.UtcDateTime,
            IsSlaBreached = batch.CompletedAt.HasValue && batch.CompletedAt.Value > deadline,
            TotalRequests = requests.Count(),
            QueuedCount = requests.Count(r => r.Status == RequestStatuses.Queued),
            RunningCount = requests.Count(r => r.Status == RequestStatuses.Running),
            CompletedCount = requests.Count(r => r.Status == RequestStatuses.Completed),
            FailedCount = requests.Count(r => r.Status == RequestStatuses.Failed),
            Notes = batch.ErrorMessage
        };
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

