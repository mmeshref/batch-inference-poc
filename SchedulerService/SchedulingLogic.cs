using Shared;

namespace SchedulerService;

public static class SchedulingLogic
{
    public static string DetermineGpuPool(
        DateTimeOffset createdAtUtc,
        TimeSpan completionWindow,
        DateTimeOffset nowUtc,
        TimeSpan slaEscalationThreshold,
        string currentPool)
    {
        var deadline = createdAtUtc + completionWindow;
        var timeToDeadline = deadline - nowUtc;

        if (timeToDeadline <= slaEscalationThreshold &&
            string.Equals(currentPool, "spot", StringComparison.OrdinalIgnoreCase))
        {
            return "dedicated";
        }

        return currentPool;
    }
}

