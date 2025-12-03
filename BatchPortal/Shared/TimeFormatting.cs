using System;

namespace BatchPortal.Shared;

public static class TimeFormatting
{
    public static string ToRelative(DateTimeOffset? value) =>
        value.HasValue ? ToRelative(value.Value) : "-";

    public static string ToRelative(DateTimeOffset value)
    {
        var now = DateTimeOffset.UtcNow;
        var delta = now - value.ToUniversalTime();

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

