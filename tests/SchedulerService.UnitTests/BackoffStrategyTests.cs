using System;
using FluentAssertions;
using GpuWorker;
using Xunit;

namespace SchedulerService.UnitTests;

public class BackoffStrategyTests
{
    [Fact]
    public void NextDelay_Should_Start_At_250ms()
    {
        var strategy = new BackoffStrategy();

        strategy.NextDelay().Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void NextDelay_Should_Double_Until_Max()
    {
        var strategy = new BackoffStrategy();

        var delays = new[]
        {
            strategy.NextDelay(), // 250
            strategy.NextDelay(), // 500
            strategy.NextDelay(), // 1s
            strategy.NextDelay(), // 2s
            strategy.NextDelay(), // 4s
            strategy.NextDelay(), // 8s
            strategy.NextDelay()  // 10s cap
        };

        delays[0].Should().Be(TimeSpan.FromMilliseconds(250));
        delays[1].Should().Be(TimeSpan.FromMilliseconds(500));
        delays[2].Should().Be(TimeSpan.FromSeconds(1));
        delays[3].Should().Be(TimeSpan.FromSeconds(2));
        delays[4].Should().Be(TimeSpan.FromSeconds(4));
        delays[5].Should().Be(TimeSpan.FromSeconds(8));
        delays[6].Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Reset_Should_Return_To_Initial()
    {
        var strategy = new BackoffStrategy();

        strategy.NextDelay();
        strategy.NextDelay();
        strategy.Reset();

        strategy.NextDelay().Should().Be(TimeSpan.FromMilliseconds(250));
    }
}

