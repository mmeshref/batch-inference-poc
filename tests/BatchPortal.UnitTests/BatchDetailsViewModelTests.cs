using System;
using System.Collections.Generic;
using BatchPortal.Mapping;
using Shared;
using Xunit;

namespace BatchPortal.UnitTests;

public class BatchDetailsViewModelTests
{
    [Fact]
    public void MapToViewModel_BeforeSla_ShouldNotBreach()
    {
        var createdAt = new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var completionWindow = TimeSpan.FromHours(24);
        var completedAt = createdAt.AddHours(12);
        var batch = BuildBatch(createdAt, completionWindow, completedAt);

        var vm = BatchDetailsMapper.Map(batch);

        Assert.Equal(4, vm.TotalRequests);
        Assert.Equal(1, vm.QueuedRequests);
        Assert.Equal(1, vm.RunningRequests);
        Assert.Equal(1, vm.CompletedRequests);
        Assert.Equal(1, vm.FailedRequests);
        Assert.Equal((createdAt + completionWindow).UtcDateTime, vm.DeadlineUtc);
        Assert.False(vm.IsSlaBreached);
        Assert.False(vm.HasOutputFile);
    }

    [Fact]
    public void MapToViewModel_AfterSla_ShouldBreach()
    {
        var createdAt = new DateTimeOffset(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var completionWindow = TimeSpan.FromHours(24);
        var completedAt = createdAt.AddHours(30);
        var batch = BuildBatch(createdAt, completionWindow, completedAt);

        var vm = BatchDetailsMapper.Map(batch);

        Assert.True(vm.IsSlaBreached);
        Assert.Equal((createdAt + completionWindow).UtcDateTime, vm.DeadlineUtc);
    }

    [Fact]
    public void MapToViewModel_WithOutputFile_SetsFlags()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var completionWindow = TimeSpan.FromHours(24);
        var batch = BuildBatch(createdAt, completionWindow, createdAt.AddHours(1));
        batch.OutputFileId = Guid.NewGuid();

        var vm = BatchDetailsMapper.Map(batch);

        Assert.True(vm.HasOutputFile);
        Assert.Equal(batch.OutputFileId, vm.OutputFileId);
    }

    private static BatchEntity BuildBatch(DateTimeOffset createdAt, TimeSpan completionWindow, DateTimeOffset? completedAt)
    {
        var batchId = Guid.NewGuid();

        var requests = new List<RequestEntity>
        {
            BuildRequest(batchId, line: 1, RequestStatuses.Queued, createdAt),
            BuildRequest(batchId, line: 2, RequestStatuses.Running, createdAt.AddMinutes(5)),
            BuildRequest(batchId, line: 3, RequestStatuses.Completed, createdAt.AddMinutes(10)),
            BuildRequest(batchId, line: 4, RequestStatuses.Failed, createdAt.AddMinutes(15))
        };

        return new BatchEntity
        {
            Id = batchId,
            UserId = "user",
            InputFileId = Guid.NewGuid(),
            OutputFileId = null,
            Status = RequestStatuses.Completed,
            Endpoint = "test-endpoint",
            CompletionWindow = completionWindow,
            Priority = 1,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt,
            StartedAt = createdAt.AddHours(1),
            CompletedAt = completedAt,
            ErrorMessage = null,
            Requests = requests
        };
    }

    private static RequestEntity BuildRequest(Guid batchId, int line, string status, DateTimeOffset createdAt)
    {
        return new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            LineNumber = line,
            InputPayload = "{}",
            OutputPayload = null,
            Status = status,
            GpuPool = GpuPools.Spot,
            CreatedAt = createdAt
        };
    }
}

