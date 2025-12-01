using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BatchPortal.Pages.Batches;

public sealed class IndexModel : PageModel
{
    private readonly BatchDbContext _dbContext;

    public IndexModel(BatchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<BatchListItem> Batches { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Batches = await _dbContext.Batches
            .Include(b => b.Requests)
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
            .Select(b => new BatchListItem(
                b.Id,
                b.UserId,
                b.Status,
                b.GpuPool,
                b.Requests.Count,
                b.CreatedAt,
                b.CompletedAt))
            .ToListAsync();
    }
}

public sealed record BatchListItem(
    Guid Id,
    string UserId,
    string Status,
    string GpuPool,
    int TotalRequests,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

