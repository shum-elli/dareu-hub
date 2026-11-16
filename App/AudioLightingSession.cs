using DareuLocal.Core;
using NAudio.Wave;
using System.Diagnostics;
namespace DareuLocal.App;
public sealed class AudioLightingSession
{
    readonly TgKeyboard device; readonly WasapiLoopbackCapture capture; readonly LightingState original; readonly CancellationTokenSource cancel = new(); readonly Task loop; readonly int rows, columns;
    sealed record Parameters(int Mode, byte[] Color, double Gain, double Brightness);
    Parameters parameters;
    int stopping; float level; long lastSignal; public string EndpointPath
    {
        get;
    }
    public string? Error
    {
        get; private set;
    }
    public double Level => Volatile.Read(ref level);
    AudioLightingSession(DeviceEndpoint endpoint, int mode, byte[] color, double gain, double brightness)
    {
        EndpointPath = endpoint.Path;
        parameters = CreateParameters(mode, color, gain, brightness);
        device = new(endpoint);
        capture = new();
        original = null!;
        try
        {
            device.ReadState();
            original = device.ReadLights().First(x => x.Region == 1);
            (rows, columns) = device.MatrixSize();
            var format = capture.WaveFormat;
            if (format.BitsPerSample is not (16 or 32))
                throw new IOException("当前音频输出格式不支持律动，请使用 16 或 32 位输出");
            capture.DataAvailable += (s, e) => { double sum = 0; int count = 0; bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"); int bytes = format.BitsPerSample / 8; for (int i = 0; i + bytes <= e.BytesRecorded; i += bytes) { double sample = bytes == 2 ? BitConverter.ToInt16(e.Buffer, i) / 32768.0 : isFloat ? BitConverter.ToSingle(e.Buffer, i) : BitConverter.ToInt32(e.Buffer, i) / 2147483648.0; if (double.IsFinite(sample)) { sum += sample * sample; count++; } } Volatile.Write(ref level, count > 0 ? (float)Math.Clamp(Math.Sqrt(sum / count) * Volatile.Read(ref parameters).Gain, 0, 1) : 0); Interlocked.Exchange(ref lastSignal, Stopwatch.GetTimestamp()); };
            capture.RecordingStopped += (s, e) => { if (e.Exception != null) { Error = e.Exception.Message; cancel.Cancel(); } };
            device.SetFrameEffect(18);
            capture.StartRecording();
            loop = Task.Run(RenderLoop);
        }
        catch { try { if (original != null) device.SetLight(original); } catch { } capture.Dispose(); device.Dispose(); throw; }
    }
    static Parameters CreateParameters(int mode, byte[] color, double gain, double brightness)
    {
        if (mode is < 0 or > 1 || color.Length != 3 || !double.IsFinite(gain) || gain is < 1 or > 30 ||
            !double.IsFinite(brightness) || brightness is < 0 or > 1)
            throw new ArgumentException("音乐律动参数无效");
        return new(mode, color.ToArray(), gain, brightness);
    }
    public void UpdateParameters(int mode, byte[] color, double gain, double brightness) =>
        Volatile.Write(ref parameters, CreateParameters(mode, color, gain, brightness));
    public static Task<AudioLightingSession> Start(DeviceEndpoint endpoint, int mode, byte[] color, double gain, double brightness) => Task.Run(() => new AudioLightingSession(endpoint, mode, color, gain, brightness));
    async Task RenderLoop()
    {
        try
        {
            using var clock = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await clock.WaitForNextTickAsync(cancel.Token).ConfigureAwait(false))
            {
                double energy = Stopwatch.GetElapsedTime(Interlocked.Read(ref lastSignal)).TotalMilliseconds > 350 ? 0 : Level;
                var settings = Volatile.Read(ref parameters);
                device.SendFrame(AudioFramePainter.Paint(rows, columns, energy, settings.Mode, settings.Color, settings.Brightness));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = ex.Message; }
        finally { try { device.SetLight(original); } catch (Exception ex) { Error = "停止律动后恢复灯效失败：" + ex.Message; } device.Dispose(); }
    }
    public async Task Stop()
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0)
        {
            await loop.ConfigureAwait(false);
            return;
        }
        cancel.Cancel();
        try
        {
            capture.StopRecording();
        }
        catch { }
        await loop.ConfigureAwait(false);
        capture.Dispose();
    }
}
