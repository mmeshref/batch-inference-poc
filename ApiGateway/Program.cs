using Microsoft.EntityFrameworkCore;
using Shared;
using System.IO;
using Prometheus;
using ApiGateway;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ApiGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<BatchDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string not configured.");
    options.UseNpgsql(connectionString);
});

var ensureSchema =
    builder.Configuration.GetValue<bool?>("Migrations:ApplyOnStartup") ?? false;

builder.Services.AddScoped<ApiGateway.Services.IFileService, ApiGateway.Services.FileService>();
builder.Services.AddScoped<ApiGateway.Services.IBatchService, ApiGateway.Services.BatchService>();

builder.Services.AddHostedService<BatchMetricsUpdater>();

var app = builder.Build();

if (ensureSchema)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupSchema");

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
        logger.LogInformation("Ensuring BatchDbContext schema exists via EnsureCreated().");
        db.Database.EnsureCreated();
        logger.LogInformation("Database schema ensured successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ensure database schema on startup.");
        throw;
    }
}

// Prometheus HTTP metrics + /metrics endpoint
app.UseHttpMetrics();
app.MapMetrics(); // exposes /metrics


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/v1/files", async (
    HttpRequest request,
    IFileService fileService,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Content-Type must be multipart/form-data.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var uploadedFile = form.Files.GetFile("file");
    if (uploadedFile is null || uploadedFile.Length == 0)
    {
        return Results.BadRequest("File is required.");
    }

    await using var stream = uploadedFile.OpenReadStream();
    var fileEntity = await fileService.SaveFileAsync(
        userId,
        uploadedFile.FileName,
        stream,
        purpose: "batch",
        cancellationToken);

    return Results.Ok(new { id = fileEntity.Id });
});

app.MapGet("/v1/files/{id:guid}/raw", async (
    Guid id,
    HttpRequest request,
    BatchDbContext dbContext,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    var file = await dbContext.Files
        .AsNoTracking()
        .SingleOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);

    if (file is null)
    {
        return Results.NotFound();
    }

    var basePath = configuration["Storage:BasePath"] ?? "/tmp/dwb-files";
    var fullPath = Path.IsPathRooted(file.StoragePath)
        ? file.StoragePath
        : Path.Combine(basePath, file.StoragePath);

    if (!System.IO.File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    const string contentType = "application/octet-stream";
    return Results.File(fullPath, contentType, file.Filename);
});

app.MapPost("/v1/batches", async (
    HttpRequest request,
    IBatchService batchService,
    CreateBatchRequest createRequest,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var userError))
    {
        return userError!;
    }

    var gpuPool = GetGpuPool(createRequest.Metadata);
    var priority = GetPriority(createRequest.Metadata);
    var completionWindow = ParseCompletionWindow(createRequest.CompletionWindow);

    BatchEntity batch;
    try
    {
        batch = await batchService.CreateBatchAsync(
            userId,
            createRequest.InputFileId,
            gpuPool,
            createRequest.UserName,
            completionWindow,
            priority,
            cancellationToken);
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }

    return Results.Ok(new { id = batch.Id, status = batch.Status });
});

app.MapGet("/v1/batches/{id:guid}", async (
    HttpRequest request,
    BatchDbContext dbContext,
    Guid id,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var userError))
    {
        return userError!;
    }

    var batch = await dbContext.Batches
        .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken);

    if (batch is null)
    {
        return Results.NotFound();
    }

    var requestCounts = await dbContext.Requests
        .Where(r => r.BatchId == id)
        .GroupBy(r => 1)
        .Select(g => new
        {
            Total = g.Count(),
            Queued = g.Count(r => r.Status == RequestStatuses.Queued),
            Running = g.Count(r => r.Status == RequestStatuses.Running),
            Completed = g.Count(r => r.Status == RequestStatuses.Completed),
            Failed = g.Count(r => r.Status == RequestStatuses.Failed)
        })
        .FirstOrDefaultAsync(cancellationToken) ?? new { Total = 0, Queued = 0, Running = 0, Completed = 0, Failed = 0 };

    var response = new BatchResponse(
        batch.Id,
        batch.Status,
        batch.Endpoint,
        batch.GpuPool,
        batch.CreatedAt,
        batch.StartedAt,
        batch.CompletedAt,
        requestCounts.Total,
        requestCounts.Queued,
        requestCounts.Running,
        requestCounts.Completed,
        requestCounts.Failed);

    return Results.Ok(response);
});

app.MapGet("/v1/batches/{id:guid}/output", async (
    Guid id,
    HttpRequest request,
    BatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    var batch = await dbContext.Batches
        .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken);

    if (batch is null)
    {
        return Results.NotFound("Batch not found.");
    }

    if (!batch.OutputFileId.HasValue)
    {
        return Results.BadRequest("Batch output not ready yet.");
    }

    var outputFile = await dbContext.Files
        .FirstOrDefaultAsync(f => f.Id == batch.OutputFileId.Value && f.UserId == userId, cancellationToken);

    if (outputFile is null || !File.Exists(outputFile.StoragePath))
    {
        return Results.NotFound("Output file not found.");
    }

    var stream = File.OpenRead(outputFile.StoragePath);
    return Results.File(stream, contentType: "text/plain");
});

app.MapGet("/v1/requests/{id:guid}", async (
    Guid id,
    HttpRequest request,
    BatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    var requestEntity = await dbContext.Requests
        .Include(r => r.Batch)
        .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    if (requestEntity is null || requestEntity.Batch is null || requestEntity.Batch.UserId != userId)
    {
        return Results.NotFound();
    }

    var response = new RequestResponse(
        requestEntity.Id,
        requestEntity.BatchId,
        requestEntity.LineNumber,
        requestEntity.Status,
        requestEntity.GpuPool,
        requestEntity.InputPayload,
        requestEntity.OutputPayload,
        requestEntity.CreatedAt,
        requestEntity.StartedAt,
        requestEntity.CompletedAt,
        requestEntity.ErrorMessage,
        requestEntity.AssignedWorker);

    return Results.Ok(response);
});

app.MapPost("/v1/requests/{id:guid}/retry", async (
    Guid id,
    HttpRequest request,
    BatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    var requestEntity = await dbContext.Requests
        .Include(r => r.Batch)
        .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    if (requestEntity is null || requestEntity.Batch is null || requestEntity.Batch.UserId != userId)
    {
        return Results.NotFound();
    }

    // Only allow retrying failed requests
    if (requestEntity.Status != RequestStatuses.Failed)
    {
        return Results.BadRequest(new { error = "Only failed requests can be retried." });
    }

    // Reset the request to queued status
    requestEntity.Status = RequestStatuses.Queued;
    requestEntity.ErrorMessage = null;
    requestEntity.StartedAt = null;
    requestEntity.CompletedAt = null;
    requestEntity.AssignedWorker = null;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { message = "Request queued for retry", requestId = id });
});

app.MapPost("/v1/batches/{id:guid}/cancel", async (
    Guid id,
    HttpRequest request,
    BatchDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var errorResult))
    {
        return errorResult!;
    }

    var batch = await dbContext.Batches
        .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken);

    if (batch is null)
    {
        return Results.NotFound();
    }

    if (batch.Status is "completed" or "failed" or "cancelled")
    {
        return Results.BadRequest($"Cannot cancel batch with status '{batch.Status}'.");
    }

    batch.Status = "cancelled";
    batch.CompletedAt = DateTimeOffset.UtcNow;
    batch.ErrorMessage = "Batch cancelled by user";

    var cancelledCount = await dbContext.Requests
        .Where(r => r.BatchId == id && r.Status == RequestStatuses.Queued)
        .ExecuteUpdateAsync(
            setters => setters
                .SetProperty(r => r.Status, RequestStatuses.Cancelled)
                .SetProperty(r => r.ErrorMessage, "Batch cancelled by user")
                .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow),
            cancellationToken);

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { id = batch.Id, status = batch.Status, cancelledRequests = cancelledCount });
});


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
    db.Database.EnsureCreated();
}

app.Run();

static bool TryGetUserId(HttpRequest request, out string userId, out IResult? errorResult)
{
    if (!request.Headers.TryGetValue("X-User-Id", out var userIdValues) ||
        Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(userIdValues) ||
        string.IsNullOrWhiteSpace(userIdValues.ToString()))
    {
        userId = string.Empty;
        errorResult = Results.BadRequest("Missing or empty X-User-Id header.");
        return false;
    }

    userId = userIdValues.ToString();
    errorResult = null;
    return true;
}

static string GetGpuPool(Dictionary<string, string>? metadata)
{
    if (metadata is not null &&
        metadata.TryGetValue("priority", out var priorityValue) &&
        string.Equals(priorityValue, "high", StringComparison.OrdinalIgnoreCase))
    {
        return "dedicated";
    }

    return "spot";
}

static int GetPriority(Dictionary<string, string>? metadata)
{
    if (metadata is not null &&
        metadata.TryGetValue("priority", out var priorityValue) &&
        string.Equals(priorityValue, "high", StringComparison.OrdinalIgnoreCase))
    {
        return 10;
    }

    return 1;
}

static TimeSpan ParseCompletionWindow(string? input)
{
    if (string.IsNullOrWhiteSpace(input))
    {
        return TimeSpan.FromHours(24);
    }

    return TimeSpan.TryParse(input, out var parsed)
        ? parsed
        : TimeSpan.FromHours(24);
}

public sealed record CreateBatchRequest(
    Guid InputFileId,
    string? Endpoint,
    string CompletionWindow,
    Dictionary<string, string>? Metadata,
    string? UserName
);

public sealed record BatchResponse(
    Guid Id,
    string Status,
    string Endpoint,
    string GpuPool,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalRequests,
    int QueuedRequests,
    int RunningRequests,
    int CompletedRequests,
    int FailedRequests
);

public sealed record RequestResponse(
    Guid Id,
    Guid BatchId,
    int LineNumber,
    string Status,
    string GpuPool,
    string InputPayload,
    string? OutputPayload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    string? AssignedWorker
);

public partial class Program { }