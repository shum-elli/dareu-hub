using DareuLocal.Core;
using NAudio.Wave;
using System.Diagnostics;
namespace DareuLocal.App;
public sealed class AudioLightingSession
{
    readonly TgKeyboard device;
    readonly WasapiLoopbackCapture capture;
    readonly LightingState original;
    readonly CancellationTokenSource cancel = new();
    readonly Task loop;
    readonly int rows, columns;
    readonly bool wireless;
    readonly float[] sampleRing = new float[1024];
    readonly object ringLock = new();
    int ringPos;

    sealed record Parameters(int Mode, byte[] Color, double Gain, double Brightness);
    Parameters parameters;
    int stopping; float level; long lastSignal;
    public string EndpointPath { get; }
    public string? Error { get; private set; }
    public double Level => Volatile.Read(ref level);

    AudioLightingSession(DeviceEndpoint endpoint, int mode, byte[] color, double gain, double brightness)
    {
        EndpointPath = endpoint.Path;
        wireless = endpoint.Wireless;
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

            int channels = Math.Max(1, format.Channels);
            int bytesPerSample = format.BitsPerSample / 8;
            int frameBytes = bytesPerSample * channels;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");

            capture.DataAvailable += (_, e) =>
            {
                double sum = 0;
                int count = 0;
                lock (ringLock)
                {
                    for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
                    {
                        double mono = 0;
                        int validCh = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            int pos = offset + ch * bytesPerSample;
                            double s = bytesPerSample == 2
                                ? BitConverter.ToInt16(e.Buffer, pos) / 32768.0
                                : isFloat
                                    ? BitConverter.ToSingle(e.Buffer, pos)
                                    : BitConverter.ToInt32(e.Buffer, pos) / 2147483648.0;
                            if (double.IsFinite(s))
                            {
                                mono += s;
                                validCh++;
                            }
                        }
                        if (validCh > 0)
                        {
                            float sample = (float)(mono / validCh);
                            sampleRing[ringPos] = sample;
                            ringPos = (ringPos + 1) % sampleRing.Length;
                            sum += sample * sample;
                            count++;
                        }
                    }
                }
                Volatile.Write(ref level, count > 0 ? (float)Math.Clamp(Math.Sqrt(sum / count) * 3.0 * Volatile.Read(ref parameters).Gain, 0, 1) : 0);
                Interlocked.Exchange(ref lastSignal, Stopwatch.GetTimestamp());
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Error = e.Exception.Message; cancel.Cancel(); } };
            device.SetFrameEffect(18);
            capture.StartRecording();
            loop = Task.Run(RenderLoop);
        }
        catch { try { if (original != null) device.SetLight(original); } catch { } capture.Dispose(); device.Dispose(); throw; }
    }

    static Parameters CreateParameters(int mode, byte[] color, double gain, double brightness)
    {
        if (mode is < 0 or > 4 || color.Length != 3 || !double.IsFinite(gain) || gain is < 1 or > 60 ||
            !double.IsFinite(brightness) || brightness is < 0 or > 1)
            throw new ArgumentException("音乐律动参数无效");
        return new(mode, color.ToArray(), gain, brightness);
    }

    public void UpdateParameters(int mode, byte[] color, double gain, double brightness) =>
        Volatile.Write(ref parameters, CreateParameters(mode, color, gain, brightness));

    public static Task<AudioLightingSession> Start(DeviceEndpoint endpoint, int mode, byte[] color, double gain, double brightness) =>
        Task.Run(() => new AudioLightingSession(endpoint, mode, color, gain, brightness));

    void CopyOrderedSamples(float[] destination)
    {
        lock (ringLock)
        {
            int tailLen = sampleRing.Length - ringPos;
            Array.Copy(sampleRing, ringPos, destination, 0, tailLen);
            Array.Copy(sampleRing, 0, destination, tailLen, ringPos);
        }
    }

    async Task RenderLoop()
    {
        var ordered = new float[sampleRing.Length];
        try
        {
            using var clock = new PeriodicTimer(TimeSpan.FromMilliseconds(wireless ? 100 : 45));
            while (await clock.WaitForNextTickAsync(cancel.Token).ConfigureAwait(false))
            {
                var settings = Volatile.Read(ref parameters);
                bool silent = Stopwatch.GetElapsedTime(Interlocked.Read(ref lastSignal)).TotalMilliseconds > 350;
                if (settings.Mode is 0 or 1)
                {
                    double energy = silent ? 0 : Level;
                    device.SendFrame(AudioFramePainter.Paint(rows, columns, energy, settings.Mode, settings.Color, settings.Brightness));
                }
                else
                {
                    double[] bands;
                    if (silent)
                    {
                        bands = new double[columns];
                    }
                    else
                    {
                        CopyOrderedSamples(ordered);
                        bands = SimpleFft.ComputeBands(ordered, columns, settings.Gain);
                    }
                    device.SendFrame(AudioFramePainter.PaintSpectrum(rows, columns, bands, settings.Mode, settings.Color, settings.Brightness));
                }
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
