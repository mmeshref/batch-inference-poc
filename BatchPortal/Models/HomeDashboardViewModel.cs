using System;
using System.Collections.Generic;

namespace BatchPortal.Models;

public class HomeDashboardViewModel
{
    public int TotalBatches { get; set; }
    public int CompletedLast24h { get; set; }
    public int FailedLast24h { get; set; }
    public int InProgress { get; set; }
    public IReadOnlyList<RecentBatchItem> RecentBatches { get; set; } = Array.Empty<RecentBatchItem>();
    public bool DbHealthy { get; set; }
    public bool ApiGatewayHealthy { get; set; }

    public class RecentBatchItem
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string GpuPool { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsSlaBreached { get; set; }
    }
}

