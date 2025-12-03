using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BatchPortal.Pages;

public class IndexModel : PageModel
{
    private readonly BatchDbContext _dbContext;

    public IndexModel(BatchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public HomeDashboardViewModel Dashboard { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var batches = await _dbContext.Batches
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dashboard = BuildDashboard(batches, now);
        var dbHealthy = await CheckDbHealthAsync(cancellationToken);

        dashboard.DbHealthy = dbHealthy;
        dashboard.ApiGatewayHealthy = true; // TODO: ping ApiGateway health endpoint once available.
        Dashboard = dashboard;

        return Page();
    }

    private async Task<bool> CheckDbHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await _dbContext.Batches.AsNoTracking().Take(1).AnyAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static HomeDashboardViewModel BuildDashboard(IEnumerable<BatchEntity> batches, DateTimeOffset now)
    {
        var batchList = batches.ToList();
        var windowStart = now.AddHours(-24);

        var totalBatches = batchList.Count;
        var completedLast24h = batchList.Count(b =>
            b.Status == RequestStatuses.Completed && b.CompletedAt.HasValue && b.CompletedAt.Value >= windowStart);
        var failedLast24h = batchList.Count(b =>
            b.Status == RequestStatuses.Failed && b.CompletedAt.HasValue && b.CompletedAt.Value >= windowStart);
        var inProgress = batchList.Count(b =>
            b.Status == RequestStatuses.Queued || b.Status == RequestStatuses.Running);

        var recentItems = batchList
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .Select(b => new HomeDashboardViewModel.RecentBatchItem
            {
                Id = b.Id,
                UserId = b.UserId,
                Status = b.Status,
                GpuPool = b.GpuPool,
                CreatedAt = b.CreatedAt,
                CompletedAt = b.CompletedAt,
                IsSlaBreached = b.CompletedAt.HasValue && b.CompletedAt.Value > b.CreatedAt + b.CompletionWindow
            })
            .ToList();

        return new HomeDashboardViewModel
        {
            TotalBatches = totalBatches,
            CompletedLast24h = completedLast24h,
            FailedLast24h = failedLast24h,
            InProgress = inProgress,
            RecentBatches = recentItems
        };
    }
}
