using System;

namespace BatchPortal.Models;

public class BatchListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GpuPool { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TotalRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int FailedRequests { get; set; }
}

