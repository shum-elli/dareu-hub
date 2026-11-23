using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class DeviceSessionQueueTests
{
    [Fact]
    public async Task EnqueueAsync_SerializesConcurrentTasks_WithoutOverlap()
    {
        using var queue = new DeviceSessionQueue();
        int active = 0;
        int maxActive = 0;
        var executedOrder = new List<int>();

        var tasks = Enumerable.Range(0, 15).Select(i => queue.EnqueueAsync(async _ =>
        {
            int now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(5);
            lock (executedOrder) executedOrder.Add(i);
            Interlocked.Decrement(ref active);
            return i;
        })).ToArray();

        int[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, maxActive);
        Assert.Equal(Enumerable.Range(0, 15).ToArray(), results);
        Assert.Equal(Enumerable.Range(0, 15).ToList(), executedOrder);
    }

    [Fact]
    public async Task EnqueueAsync_ReleasesLockWhenTaskThrows_AndSupportsCancellation()
    {
        using var queue = new DeviceSessionQueue();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.EnqueueAsync(_ => Task.FromException(new InvalidOperationException("boom"))));

        using var cts = new CancellationTokenSource();
        var blockerStarted = new TaskCompletionSource();
        var releaseBlocker = new TaskCompletionSource();

        var blockingTask = queue.EnqueueAsync(async _ =>
        {
            blockerStarted.SetResult();
            await releaseBlocker.Task;
        });

        await blockerStarted.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(_ => Task.CompletedTask, cts.Token));

        releaseBlocker.SetResult();
        await blockingTask;

        int next = await queue.EnqueueAsync(_ => Task.FromResult(42));
        Assert.Equal(42, next);
    }
}
