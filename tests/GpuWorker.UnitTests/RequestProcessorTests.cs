using System.Text.Json;
using FluentAssertions;
using GpuWorker;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace GpuWorker.UnitTests;

public class RequestProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Should_Respect_SleepMs()
    {
        var processor = new RequestProcessor(NullLogger<RequestProcessor>.Instance);
        var payload = JsonSerializer.Serialize(new { input = "hello", sleep_ms = 100 });

        var before = DateTime.UtcNow;
        var output = await processor.ProcessAsync(payload, CancellationToken.None);
        var after = DateTime.UtcNow;

        (after - before).Should().BeGreaterThan(TimeSpan.FromMilliseconds(80));
        output.Should().NotBeNullOrEmpty();
    }
}

