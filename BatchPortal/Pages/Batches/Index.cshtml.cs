using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Models;
using BatchPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BatchPortal.Pages.Batches;

public sealed class IndexModel : PageModel
{
    private readonly BatchDbContext _dbContext;
    private readonly BatchApiClient _apiClient;

    public IndexModel(BatchDbContext dbContext, BatchApiClient apiClient)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Pool { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortDir { get; set; }

    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public IReadOnlyList<string> StatusOptions { get; } = new[] { "All", "Queued", "Running", "Completed", "Failed", "Cancelled" };
    public IReadOnlyList<string> PoolOptions { get; } = new[] { "All", "spot", "dedicated" };

    public IReadOnlyList<BatchListItemViewModel> Batches { get; private set; } = Array.Empty<BatchListItemViewModel>();
    public int TotalCount { get; private set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    // Filter counts
    public Dictionary<string, int> StatusCounts { get; private set; } = new();
    public Dictionary<string, int> PoolCounts { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Validate and clamp pagination parameters
        if (CurrentPage < 1) CurrentPage = 1;
        if (PageSize < 10) PageSize = 10;
        if (PageSize > 100) PageSize = 100;

        var baseQuery = _dbContext.Batches
            .Include(b => b.Requests)
            .AsQueryable();

        // Calculate filter counts (before applying filters)
        StatusCounts["All"] = await baseQuery.CountAsync(cancellationToken);
        StatusCounts["Queued"] = await baseQuery.CountAsync(b => b.Status.ToLower() == "queued", cancellationToken);
        StatusCounts["Running"] = await baseQuery.CountAsync(b => b.Status.ToLower() == "running", cancellationToken);
        StatusCounts["Completed"] = await baseQuery.CountAsync(b => b.Status.ToLower() == "completed", cancellationToken);
        StatusCounts["Failed"] = await baseQuery.CountAsync(b => b.Status.ToLower() == "failed", cancellationToken);
        StatusCounts["Cancelled"] = await baseQuery.CountAsync(b => b.Status.ToLower() == "cancelled", cancellationToken);

        PoolCounts["All"] = await baseQuery.CountAsync(cancellationToken);
        PoolCounts["spot"] = await baseQuery.CountAsync(b => b.GpuPool == GpuPools.Spot, cancellationToken);
        PoolCounts["dedicated"] = await baseQuery.CountAsync(b => b.GpuPool == GpuPools.Dedicated, cancellationToken);

        var query = baseQuery;
        var status = (Status ?? "All").Trim();
        if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(status))
        {
            var statusLower = status.ToLower();
            query = query.Where(b => b.Status.ToLower() == statusLower);
        }

        var pool = (Pool ?? "All").Trim();
        if (!string.Equals(pool, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pool))
        {
            query = query.Where(b => b.GpuPool == pool);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(b =>
                b.UserId.Contains(search) ||
                b.Id.ToString().Contains(search));
        }

        // Get total count before pagination
        TotalCount = await query.CountAsync(cancellationToken);

        var sortBy = (SortBy ?? "created").ToLowerInvariant();
        var sortDir = (SortDir ?? "desc").ToLowerInvariant();
        var sortDesc = sortDir != "asc";

        query = (sortBy, sortDesc) switch
        {
            ("status", false) => query.OrderBy(b => b.Status).ThenByDescending(b => b.CreatedAt),
            ("status", true) => query.OrderByDescending(b => b.Status).ThenByDescending(b => b.CreatedAt),
            ("user", false) => query.OrderBy(b => b.UserId).ThenByDescending(b => b.CreatedAt),
            ("user", true) => query.OrderByDescending(b => b.UserId).ThenByDescending(b => b.CreatedAt),
            ("gpupool", false) => query.OrderBy(b => b.GpuPool).ThenByDescending(b => b.CreatedAt),
            ("gpupool", true) => query.OrderByDescending(b => b.GpuPool).ThenByDescending(b => b.CreatedAt),
            ("completed", false) => query.OrderBy(b => b.CompletedAt).ThenByDescending(b => b.CreatedAt),
            ("completed", true) => query.OrderByDescending(b => b.CompletedAt).ThenByDescending(b => b.CreatedAt),
            ("created", false) => query.OrderBy(b => b.CreatedAt),
            ("created", true) => query.OrderByDescending(b => b.CreatedAt),
            _ => query.OrderByDescending(b => b.CreatedAt)
        };

        var skip = (CurrentPage - 1) * PageSize;

        Batches = await query
            .Skip(skip)
            .Take(PageSize)
            .Select(b => new BatchListItemViewModel
            {
                Id = b.Id,
                UserId = b.UserId,
                Status = b.Status,
                GpuPool = b.GpuPool,
                Priority = b.Priority,
                CreatedAt = b.CreatedAt,
                CompletedAt = b.CompletedAt,
                TotalRequests = b.Requests.Count,
                CompletedRequests = b.Requests.Count(r => r.Status == RequestStatuses.Completed),
                FailedRequests = b.Requests.Count(r => r.Status == RequestStatuses.Failed),
                IsSlaBreached = b.CompletedAt.HasValue && b.CompletedAt.Value > b.CreatedAt + b.CompletionWindow
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.Batches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (batch is null)
        {
            return RedirectToPage();
        }

        await _apiClient.CancelBatchAsync(id, batch.UserId, cancellationToken);

        // Preserve current page and filters
        return RedirectToPage(new
        {
            CurrentPage,
            PageSize,
            Status,
            Pool,
            Search,
            SortBy,
            SortDir
        });
    }
}
