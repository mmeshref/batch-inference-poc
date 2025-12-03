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

    public BatchEntity? Batch { get; private set; }
    public IReadOnlyList<RequestViewModel> Requests { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool HasEscalatedRequests { get; private set; }
    public int EscalatedRequestCount { get; private set; }
    public DateTimeOffset? FirstEscalationAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Batch = await _dbContext.Batches
            .Include(b => b.Requests)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (Batch is null)
        {
            ErrorMessage = $"Batch {id} was not found in the database.";
            _logger.LogWarning("Batch details requested for id {BatchId} but batch was not found in the database.", id);
            return Page();
        }

        Requests = Batch.Requests
            .OrderBy(r => r.LineNumber)
            .Select(r => new RequestViewModel(
                r.Id,
                r.LineNumber,
                r.Status,
                r.GpuPool,
                r.InputPayload,
                r.OutputPayload,
                r.ErrorMessage,
                r.StartedAt))
            .ToList();

        var escalatedRequests = Batch.Requests
            .Where(r =>
                string.Equals(r.GpuPool, "dedicated", StringComparison.OrdinalIgnoreCase) &&
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

public sealed record RequestViewModel(
    Guid Id,
    int LineNumber,
    string Status,
    string? GpuPool,
    string InputPayload,
    string? OutputPayload,
    string? ErrorMessage,
    DateTimeOffset? StartedAt)
{
    public bool WasRequeuedFromSpot =>
        !string.IsNullOrEmpty(ErrorMessage) &&
        ErrorMessage.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(GpuPool, "dedicated", StringComparison.OrdinalIgnoreCase);
}
}

