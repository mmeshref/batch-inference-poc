using Microsoft.EntityFrameworkCore;
using Shared;
using System.IO;
using Prometheus;
using ApiGateway;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<BatchDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string not configured.");
    options.UseNpgsql(connectionString);
});

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
    BatchDbContext dbContext,
    IConfiguration configuration,
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

    // 1) Read base path with a default
    var basePath = configuration["Storage:BasePath"] ?? "/tmp/dwb-files";

    if (string.IsNullOrWhiteSpace(basePath))
    {
        return Results.BadRequest("Storage base path not configured.");
    }

    // 2) Ensure directory exists
    Directory.CreateDirectory(basePath);

    // 3) Generate id + filename + path
    var fileId = Guid.NewGuid();
    var originalExtension = Path.GetExtension(uploadedFile.FileName);
    var extension = string.Equals(originalExtension, ".jsonl", StringComparison.OrdinalIgnoreCase)
        ? ".jsonl"
        : originalExtension;
    var storedFileName = $"{fileId}{extension}";
    var destinationPath = Path.Combine(basePath, storedFileName);

    await using (var fileStream = File.Create(destinationPath))
    {
        await uploadedFile.CopyToAsync(fileStream, cancellationToken);
    }

    var fileEntity = new FileEntity
    {
        Id = fileId,
        UserId = userId,
        Filename = uploadedFile.FileName,
        StoragePath = destinationPath,
        Purpose = "batch",
        CreatedAt = DateTimeOffset.UtcNow
    };

    dbContext.Files.Add(fileEntity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { id = fileEntity.Id });
});

app.MapPost("/v1/batches", async (
    HttpRequest request,
    BatchDbContext dbContext,
    CreateBatchRequest createRequest,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(request, out var userId, out var userError))
    {
        return userError!;
    }

    var inputFile = await dbContext.Files
        .FirstOrDefaultAsync(f => f.Id == createRequest.InputFileId && f.UserId == userId, cancellationToken);

    if (inputFile is null)
    {
        return Results.NotFound();
    }

    var gpuPool = GetGpuPool(createRequest.Metadata);
    var priority = GetPriority(createRequest.Metadata);
    var completionWindow = ParseCompletionWindow(createRequest.CompletionWindow);

    var batch = new BatchEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        InputFileId = createRequest.InputFileId,
        OutputFileId = null,
        Status = "queued",
        Endpoint = createRequest.Endpoint,
        CompletionWindow = completionWindow,
        Priority = priority,
        GpuPool = gpuPool,
        CreatedAt = DateTimeOffset.UtcNow
    };

    dbContext.Batches.Add(batch);
    await dbContext.SaveChangesAsync(cancellationToken);

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
            Pending = g.Count(r => r.Status == "pending"),
            Running = g.Count(r => r.Status == "running"),
            Completed = g.Count(r => r.Status == "completed"),
            Failed = g.Count(r => r.Status == "failed")
        })
        .FirstOrDefaultAsync(cancellationToken) ?? new { Total = 0, Pending = 0, Running = 0, Completed = 0, Failed = 0 };

    var response = new BatchResponse(
        batch.Id,
        batch.Status,
        batch.Endpoint,
        batch.GpuPool,
        batch.CreatedAt,
        batch.StartedAt,
        batch.CompletedAt,
        requestCounts.Total,
        requestCounts.Pending,
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
    if (request.Headers.TryGetValue("X-User-Id", out var userIdValues) &&
        !string.IsNullOrWhiteSpace(userIdValues))
    {
        userId = userIdValues.ToString();
        errorResult = null;
        return true;
    }

    userId = string.Empty;
    errorResult = Results.BadRequest("Missing X-User-Id header.");
    return false;
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
    string Endpoint,
    string CompletionWindow,
    Dictionary<string, string>? Metadata
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
    int PendingRequests,
    int RunningRequests,
    int CompletedRequests,
    int FailedRequests
);
