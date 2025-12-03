using System;

namespace BatchPortal.Shared;

public static class TimeFormatting
{
    public static string ToRelative(DateTime? dt) =>
        dt.HasValue ? ToRelative(dt.Value) : "-";

    public static string ToRelative(DateTimeOffset? dto) =>
        dto.HasValue ? ToRelative(dto.Value.LocalDateTime) : "-";

    public static string ToRelative(DateTime dt)
    {
        var now = DateTime.UtcNow;
        var value = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        var delta = now - value;

        if (delta.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)delta.TotalSeconds)}s ago";
        }
        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }
        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }
        if (delta.TotalDays < 2)
        {
            return "Yesterday";
        }
        return $"{(int)delta.TotalDays} days ago";
    }
}

