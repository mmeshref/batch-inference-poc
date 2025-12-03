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

builder.Services.AddScoped<ApiGateway.Services.IFileService, ApiGateway.Services.FileService>();
builder.Services.AddScoped<ApiGateway.Services.IBatchService, ApiGateway.Services.BatchService>();

builder.Services.AddHostedService<BatchMetricsUpdater>();

var app = builder.Build();

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
            Queued = g.Count(r => r.Status == RequestStatus.Queued),
            Running = g.Count(r => r.Status == RequestStatus.Running),
            Completed = g.Count(r => r.Status == RequestStatus.Completed),
            Failed = g.Count(r => r.Status == RequestStatus.Failed)
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

public partial class Program { }