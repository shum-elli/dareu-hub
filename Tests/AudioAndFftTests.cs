using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class AudioAndFftTests
{
    [Fact]
    public void SimpleFft_ReturnsZeroForSilence_AndSeparatesFrequencies()
    {
        var silence = new float[1024];
        double[] silentBands = SimpleFft.ComputeBands(silence, 15, 12.0);
        Assert.Equal(15, silentBands.Length);
        Assert.All(silentBands, b => Assert.Equal(0.0, b));

        static float[] SineWave(double freqHz, double amplitude, double sampleRate = 48000)
        {
            var buf = new float[1024];
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (float)(amplitude * Math.Sin(2 * Math.PI * freqHz * i / sampleRate));
            return buf;
        }

        double[] lowBands = SimpleFft.ComputeBands(SineWave(250, 0.05), 15, 4.0);
        double[] highBands = SimpleFft.ComputeBands(SineWave(8000, 0.05), 15, 4.0);

        int lowPeak = Array.IndexOf(lowBands, lowBands.Max());
        int highPeak = Array.IndexOf(highBands, highBands.Max());

        Assert.True(highPeak > lowPeak, $"Expected high frequency peak ({highPeak}) > low frequency peak ({lowPeak})");
        Assert.All(lowBands.Concat(highBands), b => Assert.InRange(b, 0.0, 1.0));
    }

    [Fact]
    public void SimpleFft_LowAmplitudeDesktopAudio_ProducesStrongOutputAtDefaultGain()
    {
        var quietSignal = new float[1024];
        for (int i = 0; i < quietSignal.Length; i++)
            quietSignal[i] = (float)(0.03 * Math.Sin(2 * Math.PI * 1000 * i / 48000.0));

        double[] bands = SimpleFft.ComputeBands(quietSignal, 15, 12.0);
        Assert.True(bands.Max() >= 0.5, $"Expected peak >= 0.5 for 0.03 amplitude at 12x gain, got {bands.Max():F3}");
    }

    [Fact]
    public void PaintSpectrum_SupportsAllFourModes_AndBottomToTopFill()
    {
        var color = new byte[] { 200, 100, 50 };
        var bands = new double[] { 1.0, 0.0, 0.5, 0.25 };

        // Mode 0: whole-board uniform color
        var m0 = AudioFramePainter.PaintSpectrum(6, 4, bands, 0, color, 1.0);
        Assert.Equal(m0.Colors[0], m0.Colors[(5 * 4 + 3) * 3]);

        // Mode 1: horizontal volume bar
        var m1 = AudioFramePainter.PaintSpectrum(6, 4, new[] { 0.0, 0.0, 0.0, 0.0 }, 1, color, 1.0);
        Assert.All(m1.Colors, b => Assert.Equal(0, b));

        // Mode 2: vertical spectrum bars (col 0 = full height, col 1 = off, col 2 = bottom half)
        var m2 = AudioFramePainter.PaintSpectrum(6, 4, bands, 2, color, 1.0);
        // Col 0 top row (row 0) should be lit
        Assert.Equal(200, m2.Colors[(0 * 4 + 0) * 3]);
        // Col 1 bottom row (row 5) should be black
        Assert.Equal(0, m2.Colors[(5 * 4 + 1) * 3]);
        // Col 2 (0.5 * 6 = 3 rows): row 5 lit, row 0 dark
        Assert.Equal(200, m2.Colors[(5 * 4 + 2) * 3]);
        Assert.Equal(0, m2.Colors[(0 * 4 + 2) * 3]);

        // Mode 3: rainbow spectrum bars produce distinct colors across columns
        var m3 = AudioFramePainter.PaintSpectrum(6, 4, new[] { 1.0, 1.0, 1.0, 1.0 }, 3, color, 1.0);
        var col0Rgb = (m3.Colors[0], m3.Colors[1], m3.Colors[2]);
        var col2Rgb = (m3.Colors[2 * 3], m3.Colors[2 * 3 + 1], m3.Colors[2 * 3 + 2]);
        Assert.NotEqual(col0Rgb, col2Rgb);
    }
}
