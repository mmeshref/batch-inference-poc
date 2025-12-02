using BatchPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shared;

namespace BatchPortal.Pages.Batches;

public class CreateModel : PageModel
{
    private readonly BatchDbContext _dbContext;
    private readonly BatchApiClient _batchApiClient;

    public CreateModel(BatchDbContext dbContext, BatchApiClient batchApiClient)
    {
        _dbContext = dbContext;
        _batchApiClient = batchApiClient;
    }

    [BindProperty]
    public IFormFile? InputFile { get; set; }

    [BindProperty]
    public string Endpoint { get; set; } = string.Empty;

    [BindProperty]
    public int CompletionWindowHours { get; set; } = 24;

    public int ExistingBatchCount { get; private set; }

    public void OnGet()
    {
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? "mock-endpoint" : Endpoint;
        CompletionWindowHours = CompletionWindowHours <= 0 ? 24 : CompletionWindowHours;
        ExistingBatchCount = _dbContext.Batches.Count();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? "mock-endpoint" : Endpoint;

        if (CompletionWindowHours <= 0)
        {
            ModelState.AddModelError(nameof(CompletionWindowHours), "Completion window must be greater than zero.");
        }

        if (InputFile is null || InputFile.Length == 0)
        {
            ModelState.AddModelError(nameof(InputFile), "Input file is required.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            const string userId = "demo-user";

            var fileId = await _batchApiClient.UploadFileAsync(InputFile!, userId, cancellationToken);
            var batchId = await _batchApiClient.CreateBatchAsync(
                fileId,
                Endpoint,
                TimeSpan.FromHours(CompletionWindowHours),
                userId,
                cancellationToken);

            return RedirectToPage("/Batches/Details", new { id = batchId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Unable to create batch: {ex.Message}");
            return Page();
        }
    }
}

