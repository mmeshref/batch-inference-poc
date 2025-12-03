using System;

namespace BatchPortal.Shared;

public static class BadgeStyles
{
    public static BadgeStyleConfig ForStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        return value.ToLowerInvariant() switch
        {
            "queued" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-secondary text-light", string.Empty, "Queued", "Request is waiting in the queue."),
            "running" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-primary text-light", string.Empty, "Running", "Request is currently executing."),
            "completed" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-success text-light", string.Empty, "Completed", "Request finished successfully."),
            "failed" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-danger text-light", string.Empty, "Failed", "Request failed and needs attention."),
            "escalated" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-warning text-dark", string.Empty, "Escalated", "Request was escalated to dedicated capacity."),
            _ => new BadgeStyleConfig("badge rounded-pill badge-sm bg-light text-dark", string.Empty, string.IsNullOrWhiteSpace(value) ? "Unknown" : value, "Unknown status")
        };
    }

    public static BadgeStyleConfig ForGpuPool(string? pool)
    {
        var value = (pool ?? string.Empty).Trim();
        return value.ToLowerInvariant() switch
        {
            "spot" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-warning text-dark", "⚡", "Spot", "Runs on spot/preemptible GPUs (lower cost, interruption risk)."),
            "dedicated" => new BadgeStyleConfig("badge rounded-pill badge-sm bg-info text-dark", "🔒", "Dedicated", "Runs on dedicated GPUs (stable, higher cost)."),
            _ => new BadgeStyleConfig("badge rounded-pill badge-sm bg-light text-dark", string.Empty, string.IsNullOrWhiteSpace(value) ? "Unknown" : value, "Unknown pool")
        };
    }
}

public sealed record BadgeStyleConfig(string CssClass, string Icon, string Label, string Tooltip);

