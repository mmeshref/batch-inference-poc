using System;
using System.Collections.Generic;
using BatchPortal.Models;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class BatchListItemViewModelTests
{
    [Fact]
    public void FromEntity_ComputesTotalsAndSla()
    {
        var createdAt = new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var completionWindow = TimeSpan.FromHours(24);

        var underDeadline = CreateBatch(createdAt, completionWindow, completionHours: 12, requestCount: 3);
        var overDeadline = CreateBatch(createdAt, completionWindow, completionHours: 30, requestCount: 5);

        var vmUnder = BatchListItemViewModel.FromEntity(underDeadline);
        var vmOver = BatchListItemViewModel.FromEntity(overDeadline);

        Assert.Equal(3, vmUnder.TotalRequests);
        Assert.False(vmUnder.IsSlaBreached);

        Assert.Equal(5, vmOver.TotalRequests);
        Assert.True(vmOver.IsSlaBreached);
    }

    private static BatchEntity CreateBatch(DateTimeOffset createdAt, TimeSpan window, double completionHours, int requestCount)
    {
        var batch = new BatchEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user",
            InputFileId = Guid.NewGuid(),
            Status = RequestStatuses.Completed,
            Endpoint = "endpoint",
            CompletionWindow = window,
            Priority = 1,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddHours(1),
            CompletedAt = createdAt.AddHours(completionHours)
        };

        for (var i = 0; i < requestCount; i++)
        {
            batch.Requests.Add(new RequestEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LineNumber = i,
                InputPayload = "{}",
                Status = RequestStatuses.Completed,
                GpuPool = GpuPools.Spot,
                CreatedAt = createdAt
            });
        }

        return batch;
    }
}

