using System;
using System.Collections.Generic;
using BatchPortal.Mapping;
using BatchPortal.Models;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class BatchDetailsMapperTests
{
    [Fact]
    public void Map_ShouldMarkSlaBreached_WhenCompletedAfterDeadline()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var batch = BuildBatch(completionWindowHours: 24, completedHours: 30);

        var result = BatchDetailsMapper.Map(batch);

        Assert.True(result.IsSlaBreached);
    }

    [Fact]
    public void Map_ShouldNotMarkSlaBreached_WhenCompletedBeforeDeadline()
    {
        var batch = BuildBatch(completionWindowHours: 24, completedHours: 10);

        var result = BatchDetailsMapper.Map(batch);

        Assert.False(result.IsSlaBreached);
    }

    [Fact]
    public void Map_ShouldCountRequestsByStatus()
    {
        var batch = BuildBatch(
            completionWindowHours: 24,
            completedHours: 10,
            requests: new List<RequestEntity>
            {
                Request(RequestStatuses.Completed),
                Request(RequestStatuses.Completed),
                Request(RequestStatuses.Failed),
                Request(RequestStatuses.Queued),
                Request(RequestStatuses.Running)
            });

        var result = BatchDetailsMapper.Map(batch);

        Assert.Equal(5, result.TotalRequests);
        Assert.Equal(2, result.CompletedRequests);
        Assert.Equal(1, result.FailedRequests);
        Assert.Equal(1, result.QueuedRequests);
        Assert.Equal(1, result.RunningRequests);
        Assert.Equal(batch.OutputFileId, result.OutputFileId);
    }

    [Fact]
    public void Map_ShouldCreateInterruptionNotes()
    {
        var requests = new List<RequestEntity>
        {
            Request(RequestStatuses.Running, errorMessage: "Simulated spot interruption", gpuPool: GpuPools.Spot),
            Request(RequestStatuses.Running, errorMessage: "Simulated spot interruption", gpuPool: GpuPools.Dedicated)
        };

        var batch = BuildBatch(24, 10, requests);

        var result = BatchDetailsMapper.Map(batch);

        Assert.NotEmpty(result.InterruptionNotes);
    }

    private static BatchEntity BuildBatch(double completionWindowHours, double completedHours, List<RequestEntity>? requests = null)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var batch = new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user",
            InputFileId = Guid.NewGuid(),
            Status = RequestStatuses.Completed,
            Endpoint = "endpoint",
            CompletionWindow = TimeSpan.FromHours(completionWindowHours),
            Priority = 1,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddHours(1),
            CompletedAt = createdAt.AddHours(completedHours),
            ErrorMessage = null,
            Requests = requests ?? new List<RequestEntity>()
        };

        if (batch.Requests.Count == 0)
        {
            batch.Requests.Add(Request(RequestStatuses.Completed));
            batch.Requests.Add(Request(RequestStatuses.Queued));
            batch.Requests.Add(Request(RequestStatuses.Running));
            batch.Requests.Add(Request(RequestStatuses.Failed));
        }

        return batch;
    }

    private static RequestEntity Request(string status, string errorMessage = "", string gpuPool = GpuPools.Spot)
    {
        return new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            LineNumber = 1,
            InputPayload = "{}",
            OutputPayload = null,
            Status = status,
            GpuPool = gpuPool,
            CreatedAt = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage
        };
    }
}

