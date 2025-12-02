using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GpuWorker;

public sealed class RequestProcessor
{
    private readonly ILogger<RequestProcessor> _logger;

    public RequestProcessor(ILogger<RequestProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string inputPayload, CancellationToken cancellationToken)
    {
        JsonElement? payload = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(inputPayload))
            {
                payload = JsonSerializer.Deserialize<JsonElement>(inputPayload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize request payload for worker simulation.");
        }

        var delayMs = payload?.TryGetProperty("sleep_ms", out var sleepValue) == true &&
                      sleepValue.TryGetInt32(out var parsedSleepMs)
            ? Math.Clamp(parsedSleepMs, 0, 60_000)
            : 0;

        if (delayMs > 0)
        {
            _logger.LogInformation("Simulating worker delay of {Delay}ms.", delayMs);
            await Task.Delay(delayMs, cancellationToken);
        }

        var response = new
        {
            input = inputPayload,
            processed_at = DateTimeOffset.UtcNow,
            delay_ms = delayMs
        };

        return JsonSerializer.Serialize(response);
    }
}

