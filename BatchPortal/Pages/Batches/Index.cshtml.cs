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

    public IList<BatchListItemViewModel> Batches { get; private set; } = new List<BatchListItemViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = _dbContext.Batches.AsQueryable();

        if (!string.IsNullOrWhiteSpace(Status))
        {
            query = query.Where(b => b.Status == Status);
        }

        if (!string.IsNullOrWhiteSpace(UserId))
        {
            query = query.Where(b => b.UserId.Contains(UserId));
        }

        query = Sort switch
        {
            "created_asc" => query.OrderBy(b => b.CreatedAt),
            "created_desc" or null or "" => query.OrderByDescending(b => b.CreatedAt),
            _ => query.OrderByDescending(b => b.CreatedAt)
        };

        const int pageSize = 50;

        var list = await query
            .Include(b => b.Requests)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        Batches = list
            .Select(BatchListItemViewModel.FromEntity)
            .ToList();
    }
}
