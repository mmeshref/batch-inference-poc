using BatchPortal.Mapping;
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
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.Batches
            .Include(b => b.Requests)
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        Batch = BatchDetailsMapper.Map(batch);

        return Page();
    }

    internal static BatchDetailsViewModel MapToViewModel(BatchEntity batch) => BatchDetailsMapper.Map(batch);
}

