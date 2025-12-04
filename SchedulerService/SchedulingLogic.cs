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

    public static int ComputeDesiredSpotWorkerCount(int queuedSpotTasks)
    {
        if (queuedSpotTasks <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(queuedSpotTasks / 5d);
    }

    public static int ComputeDesiredDedicatedWorkerCount(int queuedDedicatedTasks)
    {
        if (queuedDedicatedTasks <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(queuedDedicatedTasks / 10d);
    }
}

