using System;
using System.Collections.Generic;
using System.Linq;
using BatchPortal.Models;
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
            CreatedAt = batch.CreatedAt.UtcDateTime,
            StartedAt = batch.StartedAt?.UtcDateTime,
            CompletedAt = batch.CompletedAt?.UtcDateTime,
            CompletionWindow = batch.CompletionWindow,
            ErrorMessage = batch.ErrorMessage,
            DeadlineUtc = deadline.UtcDateTime,
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
                var escalated = interrupted && string.Equals(r.GpuPool, GpuPools.Dedicated, StringComparison.OrdinalIgnoreCase);

                return new BatchDetailsViewModel.RequestItem
                {
                    Id = r.Id,
                    LineNumber = r.LineNumber,
                    Status = r.Status,
                    GpuPool = r.GpuPool,
                    CreatedAt = r.CreatedAt.UtcDateTime,
                    StartedAt = r.StartedAt?.UtcDateTime,
                    CompletedAt = r.CompletedAt?.UtcDateTime,
                    ErrorMessage = r.ErrorMessage,
                    WasInterruptedOnSpot = interrupted,
                    WasEscalatedToDedicated = escalated
                };
            })
            .ToList();

        var notes = new List<BatchDetailsViewModel.InterruptionNote>();
        foreach (var request in requestItems)
        {
            if (request.WasInterruptedOnSpot)
            {
                notes.Add(new BatchDetailsViewModel.InterruptionNote
                {
                    LineNumber = request.LineNumber,
                    Message = $"Request line {request.LineNumber} was interrupted on spot capacity."
                });
            }

            if (request.WasEscalatedToDedicated)
            {
                notes.Add(new BatchDetailsViewModel.InterruptionNote
                {
                    LineNumber = request.LineNumber,
                    Message = $"Request line {request.LineNumber} was requeued to dedicated GPU to protect the SLA."
                });
            }
        }

        vm.Requests = requestItems;
        vm.InterruptionNotes = notes;

        return vm;
    }
}

