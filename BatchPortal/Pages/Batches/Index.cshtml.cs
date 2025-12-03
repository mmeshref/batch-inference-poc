using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Models;
using Microsoft.AspNetCore.Mvc;
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

    public IReadOnlyList<string> StatusOptions { get; } = new[] { "All", "Queued", "Running", "Completed", "Failed" };
    public IReadOnlyList<string> PoolOptions { get; } = new[] { "All", "spot", "dedicated" };

    public IReadOnlyList<BatchListItemViewModel> Batches { get; private set; } = Array.Empty<BatchListItemViewModel>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = _dbContext.Batches
            .Include(b => b.Requests)
            .AsQueryable();

        var status = (Status ?? "All").Trim();
        if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(status))
        {
            query = query.Where(b => b.Status == status);
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

        const int pageSize = 50;

        Batches = await query
            .Take(pageSize)
            .Select(b => new BatchListItemViewModel
            {
                Id = b.Id,
                UserId = b.UserId,
                Status = b.Status,
                GpuPool = b.GpuPool,
                CreatedAt = b.CreatedAt,
                CompletedAt = b.CompletedAt,
                TotalRequests = b.Requests.Count,
                CompletedRequests = b.Requests.Count(r => r.Status == RequestStatuses.Completed),
                FailedRequests = b.Requests.Count(r => r.Status == RequestStatuses.Failed)
            })
            .ToListAsync(cancellationToken);
    }
}
