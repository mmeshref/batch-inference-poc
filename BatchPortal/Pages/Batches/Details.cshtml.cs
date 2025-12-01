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

    public BatchEntity? Batch { get; private set; }
    public IReadOnlyList<RequestEntity> Requests { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Batch = await _dbContext.Batches
            .Include(b => b.Requests)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (Batch is null)
        {
            return NotFound();
        }

        Requests = Batch.Requests
            .OrderBy(r => r.LineNumber)
            .ToList();

        return Page();
    }
}

