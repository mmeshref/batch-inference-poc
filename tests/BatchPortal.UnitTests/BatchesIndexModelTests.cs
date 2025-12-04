using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Pages.Batches;
using BatchPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public sealed class BatchesIndexModelTests
{
    [Fact]
    public async Task OnGetAsync_FiltersByStatus()
    {
        using var context = CreateContextWithData(out var seeded);
        var apiClient = CreateMockApiClient();
        var model = new IndexModel(context, apiClient)
        {
            Status = RequestStatuses.Completed
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.NotEmpty(model.Batches);
        Assert.All(model.Batches, b => Assert.Equal(RequestStatuses.Completed, b.Status));
        Assert.Contains(seeded, b => b.Status == RequestStatuses.Completed);
    }

    [Fact]
    public async Task OnGetAsync_FiltersByPool()
    {
        using var context = CreateContextWithData(out _);
        var apiClient = CreateMockApiClient();
        var model = new IndexModel(context, apiClient)
        {
            Pool = GpuPools.Spot
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.NotEmpty(model.Batches);
        Assert.All(model.Batches, b => Assert.Equal(GpuPools.Spot, b.GpuPool));
    }

    [Fact]
    public async Task OnGetAsync_FiltersBySearch()
    {
        using var context = CreateContextWithData(out _);
        var apiClient = CreateMockApiClient();
        var model = new IndexModel(context, apiClient)
        {
            Search = "search"
        };

        await model.OnGetAsync(CancellationToken.None);

        var batch = Assert.Single(model.Batches);
        Assert.Equal("search-user", batch.UserId);
    }

    [Fact]
    public async Task OnGetAsync_SortsByCreatedDescending()
    {
        using var context = CreateContextWithData(out var seeded);
        var apiClient = CreateMockApiClient();
        var model = new IndexModel(context, apiClient)
        {
            SortBy = "created",
            SortDir = "desc"
        };

        await model.OnGetAsync(CancellationToken.None);

        var expected = seeded.OrderByDescending(b => b.CreatedAt).First();
        Assert.Equal(expected.Id, model.Batches.First().Id);
    }

    [Fact]
    public async Task OnGetAsync_SortsByUserAscending()
    {
        using var context = CreateContextWithData(out _);
        var apiClient = CreateMockApiClient();
        var model = new IndexModel(context, apiClient)
        {
            SortBy = "user",
            SortDir = "asc"
        };

        await model.OnGetAsync(CancellationToken.None);

        var ordered = model.Batches.Select(b => b.UserId).ToList();
        var sorted = ordered.OrderBy(u => u, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, ordered);
    }

    private static BatchDbContext CreateContextWithData(out List<BatchEntity> seeded)
    {
        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new BatchDbContext(options);

        var now = new DateTimeOffset(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var inputFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            UserId = "seed-user",
            Filename = "input.jsonl",
            StoragePath = "/tmp/input.jsonl",
            Purpose = "batch",
            CreatedAt = now
        };

        var batches = new List<BatchEntity>
        {
            CreateBatch(inputFile.Id, "alice", RequestStatuses.Completed, GpuPools.Spot, now.AddHours(-4), now.AddHours(-3)),
            CreateBatch(inputFile.Id, "bob", RequestStatuses.Running, GpuPools.Dedicated, now.AddHours(-2)),
            CreateBatch(inputFile.Id, "search-user", RequestStatuses.Queued, GpuPools.Spot, now.AddHours(-1)),
            CreateBatch(inputFile.Id, "zz-top", RequestStatuses.Failed, GpuPools.Dedicated, now.AddHours(-6), now.AddHours(-5))
        };

        context.Files.Add(inputFile);
        context.Batches.AddRange(batches);
        context.SaveChanges();

        seeded = batches;
        return context;
    }

    private static BatchEntity CreateBatch(
        Guid fileId,
        string userId,
        string status,
        string gpuPool,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt = null)
    {
        var batch = new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InputFileId = fileId,
            OutputFileId = null,
            Status = status,
            Endpoint = "test-endpoint",
            CompletionWindow = TimeSpan.FromHours(24),
            Priority = 1,
            GpuPool = gpuPool,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(5),
            CompletedAt = completedAt,
            ErrorMessage = null
        };

        batch.Requests = new List<RequestEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = 1,
                InputPayload = "{}",
                OutputPayload = null,
                Status = status,
                GpuPool = gpuPool,
                CreatedAt = createdAt,
                StartedAt = createdAt.AddMinutes(1),
                CompletedAt = completedAt,
                ErrorMessage = null
            }
        };

        return batch;
    }

    private static BatchApiClient CreateMockApiClient()
    {
        var httpClient = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri("http://localhost")
        };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BatchApiClient>.Instance;
        return new BatchApiClient(httpClient, logger);
    }
}

