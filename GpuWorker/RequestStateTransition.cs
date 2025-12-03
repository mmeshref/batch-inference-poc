using Shared;

namespace GpuWorker;

internal static class RequestStateTransition
{
    public static void MarkCompleted(RequestEntity request, DateTimeOffset utcNow)
    {
        request.Status = RequestStatus.Completed;
        request.CompletedAt = utcNow;
        request.ErrorMessage = null;
    }

    public static void MarkTransientFailureRequeued(RequestEntity request, string? reason)
    {
        request.Status = RequestStatus.Queued;
        request.ErrorMessage = reason;
        request.StartedAt = null;
        request.CompletedAt = null;
        request.AssignedWorker = null;
    }

    public static void MarkTerminalFailure(RequestEntity request, DateTimeOffset utcNow, string reason)
    {
        request.Status = RequestStatus.Failed;
        request.CompletedAt = utcNow;
        request.ErrorMessage = reason;
    }
}

