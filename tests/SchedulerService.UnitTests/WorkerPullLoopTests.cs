using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GpuWorker;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace SchedulerService.UnitTests;

public class WorkerPullLoopTests
{
    [Fact]
    public async Task RunAsync_WhenNoWork_BackoffInvoked()
    {
        var repo = new StubRepository(Array.Empty<RequestEntity?>());
        var backoff = new TestBackoffStrategy();
        var loop = new WorkerPullLoop(NullLogger<WorkerPullLoop>.Instance, repo, "spot", "worker-1", backoff);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await RunLoopAndIgnoreCancellation(loop, cts);

        backoff.NextDelayCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_WhenWorkAvailable_ShouldNotBackoff()
    {
        var request = CreateRequest();
        using var cts = new CancellationTokenSource();

        var repo = new StubRepository(
            new[] { request },
            () =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });

        var backoff = new TestBackoffStrategy();
        var loop = new WorkerPullLoop(NullLogger<WorkerPullLoop>.Instance, repo, "spot", "worker-1", backoff);

        await RunLoopAndIgnoreCancellation(loop, cts);

        backoff.NextDelayCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ShouldResetBackoffAfterSuccess()
    {
        var request = CreateRequest();
        using var cts = new CancellationTokenSource();
        var cancelOnBackoff = new TaskCompletionSource();

        var repo = new StubRepository(
            new RequestEntity?[] { request, null },
            () => Task.CompletedTask);

        var backoff = new TestBackoffStrategy(delay =>
        {
            cancelOnBackoff.TrySetResult();
            cts.Cancel();
        });

        var loop = new WorkerPullLoop(NullLogger<WorkerPullLoop>.Instance, repo, "spot", "worker-1", backoff);

        await RunLoopAndIgnoreCancellation(loop, cts);

        await cancelOnBackoff.Task;
        backoff.Delays.Should().NotBeEmpty();
        backoff.Delays[0].Should().Be(TimeSpan.FromMilliseconds(250));
    }

    private static RequestEntity CreateRequest()
    {
        return new RequestEntity
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            LineNumber = 1,
            InputPayload = "{}",
            GpuPool = GpuPools.Spot,
            Status = RequestStatuses.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task RunLoopAndIgnoreCancellation(WorkerPullLoop loop, CancellationTokenSource cts)
    {
        try
        {
            await loop.RunAsync((_, _) => Task.CompletedTask, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected in tests that cancel the token.
        }
    }

    private sealed class StubRepository : IRequestRepository
    {
        private readonly Queue<RequestEntity?> _responses;
        private readonly Func<Task>? _onCompleted;

        public StubRepository(IEnumerable<RequestEntity?> responses, Func<Task>? onCompleted = null)
        {
            _responses = new Queue<RequestEntity?>(responses);
            _onCompleted = onCompleted;
        }

        public Task<RequestEntity?> DequeueAsync(string gpuPool, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
        }

        public Task<bool> MarkRunningAsync(RequestEntity request, string workerId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task MarkCompletedAsync(RequestEntity request, CancellationToken cancellationToken)
        {
            return _onCompleted?.Invoke() ?? Task.CompletedTask;
        }

        public Task MarkFailedAsync(RequestEntity request, string errorMessage, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestBackoffStrategy : BackoffStrategy
    {
        private readonly Action<TimeSpan>? _onDelay;

        public TestBackoffStrategy(Action<TimeSpan>? onDelay = null)
        {
            _onDelay = onDelay;
        }

        public int NextDelayCallCount { get; private set; }
        public List<TimeSpan> Delays { get; } = new();

        public override TimeSpan NextDelay()
        {
            var delay = base.NextDelay();
            NextDelayCallCount++;
            Delays.Add(delay);
            _onDelay?.Invoke(delay);
            return delay;
        }
    }
}

