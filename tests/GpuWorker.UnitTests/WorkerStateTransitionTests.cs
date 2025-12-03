using System;
using FluentAssertions;
using GpuWorker;
using Shared;
using Xunit;

namespace GpuWorker.UnitTests;

public static class RequestFactory
{
    public static RequestEntity Create()
    {
        return new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            LineNumber = 1,
            InputPayload = """{"prompt":"hello"}""",
            GpuPool = "spot",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}

public class WorkerStateTransitionTests
{
    [Fact]
    public void MarkCompleted_Should_Update_Status_And_Timestamps()
    {
        var request = RequestFactory.Create();
        request.Status = RequestStatuses.Running;
        var completedAt = DateTimeOffset.UtcNow;

        RequestStateTransition.MarkCompleted(request, completedAt);

        request.Status.Should().Be(RequestStatuses.Completed);
        request.CompletedAt.Should().Be(completedAt);
        request.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkTransientFailureRequeued_Should_Reset_Request_To_Queued()
    {
        var request = RequestFactory.Create();
        request.Status = RequestStatuses.Running;
        request.AssignedWorker = "worker-1";
        request.StartedAt = DateTimeOffset.UtcNow;
        request.CompletedAt = DateTimeOffset.UtcNow;

        const string reason = "Transient failure: spot interruption";

        RequestStateTransition.MarkTransientFailureRequeued(request, reason);

        request.Status.Should().Be(RequestStatuses.Queued);
        request.ErrorMessage.Should().Be(reason);
        request.AssignedWorker.Should().BeNull();
        request.StartedAt.Should().BeNull();
        request.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void MarkTerminalFailure_Should_Set_Status_To_Failed()
    {
        var request = RequestFactory.Create();
        request.Status = RequestStatuses.Running;
        var failedAt = DateTimeOffset.UtcNow;
        const string reason = "LLM call failed";

        RequestStateTransition.MarkTerminalFailure(request, failedAt, reason);

        request.Status.Should().Be(RequestStatuses.Failed);
        request.CompletedAt.Should().Be(failedAt);
        request.ErrorMessage.Should().Be(reason);
    }
}

