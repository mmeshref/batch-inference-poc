using System;
using FluentAssertions;
using Shared;
using Xunit;

namespace Shared.Tests;

public class RequestStatusTests
{
    [Fact]
    public void RequestStatus_Should_Expose_All_Known_States()
    {
        var statuses = Enum.GetValues<RequestStatus>();

        statuses.Should().Contain(new[]
        {
            RequestStatus.Queued,
            RequestStatus.Running,
            RequestStatus.Completed,
            RequestStatus.Failed,
            RequestStatus.DeadLettered
        });
    }

    [Fact]
    public void RequestEntity_Should_Default_Status_To_Queued()
    {
        var entity = new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            LineNumber = 1,
            InputPayload = """{"prompt":"hello"}""",
            GpuPool = "spot",
            CreatedAt = DateTimeOffset.UtcNow
        };

        entity.Status.Should().Be(RequestStatus.Queued);
    }
}

