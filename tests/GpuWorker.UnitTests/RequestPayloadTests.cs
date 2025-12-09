using System.Text.Json;
using FluentAssertions;
using GpuWorker.Models;
using Xunit;

namespace GpuWorker.UnitTests;

public class RequestPayloadTests
{
    [Fact]
    public void Deserialize_Should_Map_SleepSeconds_From_SnakeCase()
    {
        // Arrange - JSON uses snake_case (as in the actual batch files)
        var json = """{"input": "test input", "sleep_seconds": 15}""";

        // Act
        var payload = JsonSerializer.Deserialize<RequestPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.Input.Should().Be("test input");
        payload.SleepSeconds.Should().Be(15);
    }

    [Fact]
    public void Deserialize_Should_Handle_Missing_SleepSeconds()
    {
        // Arrange
        var json = """{"input": "test input"}""";

        // Act
        var payload = JsonSerializer.Deserialize<RequestPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.Input.Should().Be("test input");
        payload.SleepSeconds.Should().BeNull();
    }

    [Fact]
    public void Deserialize_Should_Handle_Zero_SleepSeconds()
    {
        // Arrange
        var json = """{"input": "test input", "sleep_seconds": 0}""";

        // Act
        var payload = JsonSerializer.Deserialize<RequestPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.Input.Should().Be("test input");
        payload.SleepSeconds.Should().Be(0);
    }

    [Fact]
    public void Deserialize_Should_Handle_Large_SleepSeconds()
    {
        // Arrange
        var json = """{"input": "test input", "sleep_seconds": 100}""";

        // Act
        var payload = JsonSerializer.Deserialize<RequestPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.Input.Should().Be("test input");
        payload.SleepSeconds.Should().Be(100);
    }

    [Fact]
    public void Deserialize_Should_Map_Input_From_Lowercase()
    {
        // Arrange - JSON uses lowercase "input" (as in the actual batch files)
        var json = """{"input": "test input", "sleep_seconds": 5}""";

        // Act
        var payload = JsonSerializer.Deserialize<RequestPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.Input.Should().Be("test input");
    }
}
