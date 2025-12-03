using System;

namespace BatchPortal.Models;

public sealed class BatchDetailsViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GpuPool { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan CompletionWindow { get; set; }
    public DateTime SlaDeadline { get; set; }
    public bool IsSlaBreached { get; set; }
    public int TotalRequests { get; set; }
    public int QueuedCount { get; set; }
    public int RunningCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public string? Notes { get; set; }
}

