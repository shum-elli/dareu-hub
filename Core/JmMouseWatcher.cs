using HidSharp;

namespace DareuLocal.Core;

/// <summary>
/// Listens on a JM mouse HID input stream for unsolicited hardware event reports (cmd 0x0A),
/// such as physical DPI button presses, without polling.
/// </summary>
public sealed class JmMouseWatcher : IDisposable
{
    readonly DeviceEndpoint endpoint;
    readonly CancellationTokenSource cts = new();
    readonly Task loop;
    HidStream? stream;
    int disposed;

    public string EndpointPath => endpoint.Path;

    public event Action<DeviceEndpoint, byte[]>? HardwareEventReceived;

    public JmMouseWatcher(DeviceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Kind != DeviceKind.Mouse || endpoint.PhysicalProtocol != 2 || !endpoint.CanConfigure)
            throw new ArgumentException("仅支持监听可配置的 JM 协议鼠标设备。", nameof(endpoint));
        this.endpoint = endpoint;
        loop = Task.Run(ListenLoop);
    }

    public static bool IsHardwareEventPacket(ReadOnlySpan<byte> report)
    {
        if (report.Length < 17)
            return false;
        if (report[0] != 0x08 || report[1] != 0x0A)
            return false;
        return report[16] == JmMouse.Checksum(report[..16]);
    }

    void ListenLoop()
    {
        try
        {
            var opened = endpoint.Device.Open();
            opened.ReadTimeout = 500;
            if (Volatile.Read(ref disposed) != 0)
            {
                opened.Dispose();
                return;
            }
            stream = opened;

            int length = Math.Max(17, endpoint.Device.GetMaxInputReportLength());
            var buffer = new byte[length];
            while (!cts.IsCancellationRequested && Volatile.Read(ref disposed) == 0)
            {
                int n;
                try
                {
                    n = opened.Read(buffer);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                if (n >= 17 && IsHardwareEventPacket(buffer.AsSpan(0, n)))
                    HardwareEventReceived?.Invoke(endpoint, buffer[..17]);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        cts.Cancel();
        try { stream?.Dispose(); } catch { }
        stream = null;
        cts.Dispose();
    }
}
