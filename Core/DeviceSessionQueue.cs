namespace DareuLocal.Core;

/// <summary>
/// Serializes asynchronous device I/O operations so dashboard polling, live DPI edits,
/// UI commands, and foreground auto-switching never contend on the same HID device.
/// </summary>
public sealed class DeviceSessionQueue : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    int disposed;

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await work(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task EnqueueAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return EnqueueAsync<object?>(async token =>
        {
            await work(token).ConfigureAwait(false);
            return null;
        }, ct);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            gate.Dispose();
    }
}
