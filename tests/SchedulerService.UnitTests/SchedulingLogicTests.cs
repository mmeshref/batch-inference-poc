using System;
using FluentAssertions;
using SchedulerService;
using Xunit;

namespace SchedulerService.UnitTests;

public class SchedulingLogicScalingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(25, 5)]
    public void ComputeDesiredSpotWorkerCount_Should_MatchExpected(int queued, int expected)
    {
        SchedulingLogic.ComputeDesiredSpotWorkerCount(queued).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    public void ComputeDesiredDedicatedWorkerCount_Should_MatchExpected(int queued, int expected)
    {
        SchedulingLogic.ComputeDesiredDedicatedWorkerCount(queued).Should().Be(expected);
    }
}

public class SchedulingLogicDeterminePoolTests
{
    [Fact]
    public void Spot_To_Dedicated_When_Within_Threshold()
    {
        var createdAt = new DateTimeOffset(2025, 12, 2, 10, 0, 0, TimeSpan.Zero);
        var completionWindow = TimeSpan.FromHours(24);
        var now = createdAt.AddHours(23);
        var threshold = TimeSpan.FromHours(2);

        var pool = SchedulingLogic.DetermineGpuPool(
            createdAt,
            completionWindow,
            now,
            threshold,
            currentPool: "spot");

        pool.Should().Be("dedicated");
    }

    [Fact]
    public void Stay_On_Spot_When_Outside_Threshold()
    {
        var createdAt = new DateTimeOffset(2025, 12, 2, 10, 0, 0, TimeSpan.Zero);
        var completionWindow = TimeSpan.FromHours(24);
        var now = createdAt.AddHours(20);
        var threshold = TimeSpan.FromHours(2);

        var pool = SchedulingLogic.DetermineGpuPool(
            createdAt,
            completionWindow,
            now,
            threshold,
            currentPool: "spot");

        pool.Should().Be("spot");
    }

    [Fact]
    public void Stay_On_Dedicated_When_Already_Dedicated()
    {
        var createdAt = new DateTimeOffset(2025, 12, 2, 10, 0, 0, TimeSpan.Zero);
        var completionWindow = TimeSpan.FromHours(24);
        var now = createdAt.AddHours(23.5);
        var threshold = TimeSpan.FromHours(2);

        var pool = SchedulingLogic.DetermineGpuPool(
            createdAt,
            completionWindow,
            now,
            threshold,
            currentPool: "dedicated");

        pool.Should().Be("dedicated");
    }
}
