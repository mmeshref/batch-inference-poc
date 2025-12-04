using System;
using System.Collections.Generic;

namespace BatchPortal.Models;

public sealed class BatchDetailsViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GpuPool { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan CompletionWindow { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSlaBreached { get; set; }
    public DateTimeOffset DeadlineUtc { get; set; }
    public int TotalRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int FailedRequests { get; set; }
    public int QueuedRequests { get; set; }
    public int RunningRequests { get; set; }
    public Guid? OutputFileId { get; set; }
    public bool HasOutputFile => OutputFileId.HasValue;
    public IReadOnlyList<string> OutputPreviewLines { get; set; } = Array.Empty<string>();
    public bool OutputPreviewTruncated { get; set; }
    public IReadOnlyList<RequestItem> Requests { get; set; } = Array.Empty<RequestItem>();
    public IReadOnlyList<InterruptionNote> InterruptionNotes { get; set; } = Array.Empty<InterruptionNote>();

    public sealed class RequestItem
    {
        public Guid Id { get; set; }
        public int LineNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public string GpuPool { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string DurationDisplay { get; set; } = "-";
        public int RetryCount { get; set; }
        public bool WasEscalated { get; set; }
        public string InputPayload { get; set; } = string.Empty;
        public string? OutputPayload { get; set; }
        public string? Notes { get; set; }
        public IReadOnlyList<GpuPoolHistoryEntry> GpuPoolHistory { get; set; } = Array.Empty<GpuPoolHistoryEntry>();
        
        // Deduplication fields
        public bool IsDeduplicated { get; set; }
        public Guid? OriginalRequestId { get; set; }
    }

    public sealed class InterruptionNote
    {
        public int LineNumber { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class GpuPoolHistoryEntry
    {
        public string Pool { get; set; } = string.Empty;
        public DateTimeOffset? OccurredAt { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

