using FluentAssertions;
using Shared;
using Xunit;
using System;

namespace Shared.Tests;

public class BatchEntityTests
{
    [Fact]
    public void GetDeadline_Should_Add_CompletionWindow_To_CreatedAt()
    {
        var createdAt = new DateTime(2025, 12, 2, 10, 0, 0, DateTimeKind.Utc);
        var window = TimeSpan.FromHours(24);

        var deadline = SlaHelpers.GetDeadline(createdAt, window);

        deadline.Should().Be(createdAt.AddHours(24));
    }
}

