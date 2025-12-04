using Microsoft.Extensions.Logging;

namespace SchedulerService;

public sealed class WorkerScaler
{
    private readonly ILogger<WorkerScaler> _logger;

    public WorkerScaler(ILogger<WorkerScaler> logger)
    {
        _logger = logger;
    }

    public ScaleDecision EvaluateScaling(
        int currentSpotWorkers,
        int currentDedicatedWorkers,
        int queuedSpotRequests,
        int queuedDedicatedRequests)
    {
        var desiredSpot = SchedulingLogic.ComputeDesiredSpotWorkerCount(queuedSpotRequests);
        var desiredDedicated = SchedulingLogic.ComputeDesiredDedicatedWorkerCount(queuedDedicatedRequests);

        var spotDecision = Compare(currentSpotWorkers, desiredSpot);
        var dedicatedDecision = Compare(currentDedicatedWorkers, desiredDedicated);

        if (spotDecision == ScaleAction.NoOp && dedicatedDecision == ScaleAction.NoOp)
        {
            return new ScaleDecision(spotDecision, dedicatedDecision, currentSpotWorkers, currentDedicatedWorkers);
        }

        if (spotDecision != ScaleAction.NoOp)
        {
            _logger.LogInformation(
                "Scaling Spot workers from {Current} → {Desired}",
                currentSpotWorkers,
                desiredSpot);
        }

        if (dedicatedDecision != ScaleAction.NoOp)
        {
            _logger.LogInformation(
                "Scaling Dedicated workers from {Current} → {Desired}",
                currentDedicatedWorkers,
                desiredDedicated);
        }

        return new ScaleDecision(
            spotDecision,
            dedicatedDecision,
            desiredSpot,
            desiredDedicated);
    }

    private static ScaleAction Compare(int current, int desired)
    {
        if (desired > current)
        {
            return ScaleAction.ScaleUp;
        }

        if (desired < current)
        {
            return ScaleAction.ScaleDown;
        }

        return ScaleAction.NoOp;
    }
}

public sealed record ScaleDecision(
    ScaleAction SpotAction,
    ScaleAction DedicatedAction,
    int DesiredSpot,
    int DesiredDedicated);

public enum ScaleAction
{
    NoOp,
    ScaleUp,
    ScaleDown
}

