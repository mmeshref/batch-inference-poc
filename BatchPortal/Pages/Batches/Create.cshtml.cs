using BatchPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Shared;

namespace BatchPortal.Pages.Batches;

public class CreateModel : PageModel
{
    private readonly BatchDbContext _dbContext;
    private readonly BatchApiClient _batchApiClient;
    private readonly string _defaultUserId;

    public CreateModel(BatchDbContext dbContext, BatchApiClient batchApiClient, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _batchApiClient = batchApiClient;
        _defaultUserId = configuration["BatchPortal:DefaultUserId"] ?? "demo-user";
    }

    [BindProperty]
    public IFormFile? InputFile { get; set; }

    [BindProperty]
    public string? UserName { get; set; }

    [BindProperty]
    public int Priority { get; set; } = 1; // Default: Normal priority

    public int ExistingBatchCount { get; private set; }

    public void OnGet()
    {
        UserName ??= _defaultUserId;
        ExistingBatchCount = _dbContext.Batches.Count();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (InputFile is null || InputFile.Length == 0)
        {
            ModelState.AddModelError(nameof(InputFile), "Input file is required.");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            ModelState.AddModelError(nameof(UserName), "User name is required.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var userId = UserName!.Trim();

            var fileId = await _batchApiClient.UploadFileAsync(InputFile!, userId, cancellationToken);
            var batchId = await _batchApiClient.CreateBatchAsync(
                fileId,
                userId,
                Priority,
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

