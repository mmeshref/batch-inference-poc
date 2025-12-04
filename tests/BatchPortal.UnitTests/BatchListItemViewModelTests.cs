using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Pages.Batches;
using BatchPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class BatchListItemViewModelTests
{
    [Fact]
    public async Task IndexModelProjection_ComputesRequestCounts()
    {
        await using var context = CreateContextWithBatch();
        var httpClient = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri("http://localhost")
        };
        var apiClient = new BatchApiClient(httpClient, NullLogger<BatchApiClient>.Instance);
        var model = new IndexModel(context, apiClient);

        await model.OnGetAsync(CancellationToken.None);

        var vm = Assert.Single(model.Batches);
        Assert.Equal(3, vm.TotalRequests);
        Assert.Equal(2, vm.CompletedRequests);
        Assert.Equal(1, vm.FailedRequests);
    }

    private static BatchDbContext CreateContextWithBatch()
    {
        var options = new DbContextOptionsBuilder<BatchDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new BatchDbContext(options);

        var createdAt = DateTimeOffset.UtcNow;
        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-files",
            Filename = "input.jsonl",
            StoragePath = "/tmp/input.jsonl",
            Purpose = "batch",
            CreatedAt = createdAt
        };

        var batch = new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = "counts-user",
            InputFileId = file.Id,
            Status = RequestStatuses.Completed,
            Endpoint = "endpoint",
            CompletionWindow = TimeSpan.FromHours(24),
            Priority = 1,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddMinutes(5),
            CompletedAt = createdAt.AddHours(2)
        };

        batch.Requests = new List<RequestEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = 1,
                InputPayload = "{}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = createdAt,
                StartedAt = createdAt.AddMinutes(1),
                CompletedAt = createdAt.AddMinutes(10)
            },
            new()
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = 2,
                InputPayload = "{}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = createdAt,
                StartedAt = createdAt.AddMinutes(2),
                CompletedAt = createdAt.AddMinutes(12)
            },
            new()
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = 3,
                InputPayload = "{}",
                Status = RequestStatuses.Failed,
                GpuPool = GpuPools.Spot,
                CreatedAt = createdAt,
                StartedAt = createdAt.AddMinutes(3),
                CompletedAt = createdAt.AddMinutes(13)
            }
        };

        context.Files.Add(file);
        context.Batches.Add(batch);
        context.SaveChanges();

        return context;
    }
}

