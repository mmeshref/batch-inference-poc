using System;
using System.Collections.Generic;
using System.Linq;
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
        Assert.Equal(createdAt + completionWindow, vm.DeadlineUtc);
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
        Assert.Equal(createdAt + completionWindow, vm.DeadlineUtc);
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

    [Fact]
    public void MapToViewModel_RequestItems_ShouldSetDurationRetryAndHistory()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var completionWindow = TimeSpan.FromHours(24);
        var batch = BuildBatch(createdAt, completionWindow, createdAt.AddHours(2));
        var request = batch.Requests.First(r => r.Status == RequestStatuses.Completed);
        request.StartedAt = createdAt.AddMinutes(5);
        request.CompletedAt = createdAt.AddMinutes(15);
        request.ErrorMessage = "Simulated spot interruption – retrying on dedicated";
        request.GpuPool = GpuPools.Dedicated;

        var vm = BatchDetailsMapper.Map(batch);
        var completedRequest = vm.Requests.First(r => r.Status == RequestStatuses.Completed);

        Assert.Equal("10 minutes", completedRequest.DurationDisplay);
        Assert.Equal(1, completedRequest.RetryCount);
        Assert.True(completedRequest.WasEscalated);
        Assert.Equal(request.InputPayload, completedRequest.InputPayload);
        Assert.Equal(request.OutputPayload, completedRequest.OutputPayload);
        Assert.NotEmpty(completedRequest.GpuPoolHistory);
        Assert.Contains(completedRequest.GpuPoolHistory, h => h.Pool == GpuPools.Spot);
        Assert.Contains(vm.InterruptionNotes, note => note.LineNumber == completedRequest.LineNumber);
    }

    [Fact]
    public void MapToViewModel_RequestWithoutCompletion_ShouldShowPlaceholderDuration()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var batch = BuildBatch(createdAt, TimeSpan.FromHours(1), null);
        var pendingRequest = batch.Requests.First(r => r.Status == RequestStatuses.Queued);

        var vm = BatchDetailsMapper.Map(batch);
        var mapped = vm.Requests.First(r => r.LineNumber == pendingRequest.LineNumber);

        Assert.Equal("-", mapped.DurationDisplay);
        Assert.Equal(0, mapped.RetryCount);
        Assert.False(mapped.WasEscalated);
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

