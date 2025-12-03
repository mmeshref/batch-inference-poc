using System;
using FluentAssertions;
using Shared;
using Xunit;

namespace Shared.Tests;

public class RequestStatusTests
{
    [Fact]
    public void RequestStatuses_Should_Expose_All_Known_States()
    {
        var statuses = new[]
        {
            RequestStatuses.Queued,
            RequestStatuses.Running,
            RequestStatuses.Completed,
            RequestStatuses.Failed,
            RequestStatuses.DeadLettered
        };

        statuses.Should().OnlyHaveUniqueItems();
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

        entity.Status.Should().Be(RequestStatuses.Queued);
    }
}

