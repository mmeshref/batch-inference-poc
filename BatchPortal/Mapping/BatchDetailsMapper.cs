using System;
using System.Collections.Generic;
using System.Linq;
using BatchPortal.Models;
using Humanizer;
using Humanizer.Localisation;
using Shared;

namespace BatchPortal.Mapping;

internal static class BatchDetailsMapper
{
    internal static BatchDetailsViewModel Map(BatchEntity batch)
    {
        var deadline = batch.CreatedAt + batch.CompletionWindow;
        var requests = batch.Requests ?? Array.Empty<RequestEntity>();

        var vm = new BatchDetailsViewModel
        {
            Id = batch.Id,
            UserId = batch.UserId,
            Status = batch.Status,
            GpuPool = batch.GpuPool,
            Priority = batch.Priority,
            CreatedAt = batch.CreatedAt,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt,
            CompletionWindow = batch.CompletionWindow,
            ErrorMessage = batch.ErrorMessage,
            DeadlineUtc = deadline,
            IsSlaBreached = batch.CompletedAt.HasValue && batch.CompletedAt.Value > deadline,
            TotalRequests = requests.Count(),
            CompletedRequests = requests.Count(r => r.Status == RequestStatuses.Completed),
            FailedRequests = requests.Count(r => r.Status == RequestStatuses.Failed),
            QueuedRequests = requests.Count(r => r.Status == RequestStatuses.Queued),
            RunningRequests = requests.Count(r => r.Status == RequestStatuses.Running),
            OutputFileId = batch.OutputFileId
        };

        var requestItems = requests
            .OrderBy(r => r.LineNumber)
            .Select(r =>
            {
                var interrupted = !string.IsNullOrEmpty(r.ErrorMessage) &&
                                  r.ErrorMessage.Contains("Simulated spot interruption", StringComparison.OrdinalIgnoreCase);
                var wasEscalated = string.Equals(r.GpuPool, GpuPools.Dedicated, StringComparison.OrdinalIgnoreCase) && interrupted;
                var durationDisplay = BuildDurationDisplay(r.StartedAt, r.CompletedAt);
                var history = BuildGpuPoolHistory(r, wasEscalated);
                var retryCount = wasEscalated || interrupted ? 1 : 0;

                return new BatchDetailsViewModel.RequestItem
                {
                    Id = r.Id,
                    LineNumber = r.LineNumber,
                    Status = r.Status,
                    GpuPool = r.GpuPool,
                    CreatedAt = r.CreatedAt,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    ErrorMessage = r.ErrorMessage,
                    DurationDisplay = durationDisplay,
                    RetryCount = retryCount,
                    WasEscalated = wasEscalated,
                    InputPayload = r.InputPayload,
                    OutputPayload = r.OutputPayload,
                    Notes = r.ErrorMessage,
                    GpuPoolHistory = history
                };
            })
            .ToList();

        var notes = BuildInterruptionNotes(requestItems);

        vm.Requests = requestItems;
        vm.InterruptionNotes = notes;

        return vm;
    }

    private static string BuildDurationDisplay(DateTimeOffset? started, DateTimeOffset? completed)
    {
        if (!started.HasValue || !completed.HasValue || completed <= started)
        {
            return "-";
        }

        var duration = completed.Value - started.Value;
        return duration.Humanize(precision: 2, minUnit: TimeUnit.Second);
    }

    private static IReadOnlyList<BatchDetailsViewModel.GpuPoolHistoryEntry> BuildGpuPoolHistory(RequestEntity request, bool wasEscalated)
    {
        var history = new List<BatchDetailsViewModel.GpuPoolHistoryEntry>();
        if (wasEscalated)
        {
            history.Add(new BatchDetailsViewModel.GpuPoolHistoryEntry
            {
                Pool = GpuPools.Spot,
                OccurredAt = request.CreatedAt,
                Description = "Initial spot allocation"
            });
        }

        history.Add(new BatchDetailsViewModel.GpuPoolHistoryEntry
        {
            Pool = request.GpuPool,
            OccurredAt = request.StartedAt ?? request.CreatedAt,
            Description = wasEscalated ? "Escalated to dedicated pool" : "Active pool"
        });

        return history;
    }

    private static IReadOnlyList<BatchDetailsViewModel.InterruptionNote> BuildInterruptionNotes(IEnumerable<BatchDetailsViewModel.RequestItem> requests)
    {
        var notes = new List<BatchDetailsViewModel.InterruptionNote>();
        foreach (var request in requests)
        {
            if (!string.IsNullOrEmpty(request.Notes))
            {
                notes.Add(new BatchDetailsViewModel.InterruptionNote
                {
                    LineNumber = request.LineNumber,
                    Message = request.Notes!
                });
            }

            if (request.WasEscalated)
            {
                notes.Add(new BatchDetailsViewModel.InterruptionNote
                {
                    LineNumber = request.LineNumber,
                    Message = $"Request line {request.LineNumber} escalated to dedicated GPUs."
                });
            }
        }

        return notes;
    }
}

