using System.Text.Json.Serialization;

namespace GpuWorker.Models;

public class RequestPayload
{
    [JsonPropertyName("input")]
    public string? Input { get; set; }
    
    [JsonPropertyName("sleep_seconds")]
    public int? SleepSeconds { get; set; }
}

