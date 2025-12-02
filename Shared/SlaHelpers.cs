namespace Shared;

public static class SlaHelpers
{
    public static DateTime GetDeadline(DateTime createdAtUtc, TimeSpan completionWindow) =>
        createdAtUtc + completionWindow;
}

