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

    [BindProperty(SupportsGet = true)]
    public int RequestPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int RequestPageSize { get; set; } = 50;

    public int TotalRequestPages { get; private set; }
    public bool HasPreviousRequestPage => RequestPage > 1;
    public bool HasNextRequestPage => RequestPage < TotalRequestPages;
    
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        // Validate and clamp pagination parameters
        if (RequestPage < 1) RequestPage = 1;
        if (RequestPageSize < 10) RequestPageSize = 10;
        if (RequestPageSize > 200) RequestPageSize = 200;

        var batch = await _dbContext.Batches
            .Include(b => b.Requests)
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        Batch = BatchDetailsMapper.Map(batch);

        // Apply pagination to requests
        var totalRequests = Batch.Requests.Count;
        TotalRequestPages = (int)Math.Ceiling((double)totalRequests / RequestPageSize);
        
        var skip = (RequestPage - 1) * RequestPageSize;
        Batch.Requests = Batch.Requests
            .Skip(skip)
            .Take(RequestPageSize)
            .ToList();

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

