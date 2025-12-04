using BatchPortal.Mapping;
using BatchPortal.Models;
using BatchPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BatchPortal.Pages.Batches;

public sealed class DetailsModel : PageModel
{
    private readonly BatchDbContext _dbContext;
    private readonly BatchApiClient _apiClient;

    public DetailsModel(BatchDbContext dbContext, BatchApiClient apiClient)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
    }

    public BatchDetailsViewModel Batch { get; private set; } = default!;
    public string? OutputFileDownloadUrl { get; private set; }
    
    [TempData]
    public string? Message { get; set; }
    
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

        if (Batch.OutputFileId.HasValue)
        {
            var (lines, truncated) = await _apiClient.GetOutputPreviewAsync(
                Batch.OutputFileId.Value,
                20,
                cancellationToken);

            Batch.OutputPreviewLines = lines;
            Batch.OutputPreviewTruncated = truncated;

            var downloadUri = _apiClient.GetOutputFileDownloadUrl(Batch.OutputFileId);
            OutputFileDownloadUrl = downloadUri?.ToString();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.Batches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        var success = await _apiClient.CancelBatchAsync(id, batch.UserId, cancellationToken);

        if (success)
        {
            Message = "Batch cancelled successfully.";
        }
        else
        {
            Message = "Could not cancel batch. It may have already completed or been cancelled.";
        }

        return RedirectToPage(new { id });
    }

    internal static BatchDetailsViewModel MapToViewModel(BatchEntity batch) => BatchDetailsMapper.Map(batch);
}

