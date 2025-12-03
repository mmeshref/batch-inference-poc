using System;
using Shared;

namespace BatchPortal.Models;

public class BatchListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GpuPool { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalRequests { get; set; }
    public bool IsSlaBreached { get; set; }

    public static BatchListItemViewModel FromEntity(BatchEntity batch)
    {
        var completionWindow = batch.CompletionWindow;
        var deadline = batch.CreatedAt + completionWindow;

        return new BatchListItemViewModel
        {
            Id = batch.Id,
            UserId = batch.UserId,
            Status = batch.Status,
            GpuPool = batch.GpuPool,
            CreatedAt = batch.CreatedAt.UtcDateTime,
            CompletedAt = batch.CompletedAt?.UtcDateTime,
            TotalRequests = batch.Requests?.Count ?? 0,
            IsSlaBreached = batch.CompletedAt.HasValue && batch.CompletedAt.Value > deadline
        };
    }
}

