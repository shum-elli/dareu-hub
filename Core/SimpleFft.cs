namespace DareuLocal.Core;

/// <summary>
/// Pure C# radix-2 Cooley-Tukey FFT spectrum analyzer for audio-reactive keyboard lighting.
/// </summary>
public static class SimpleFft
{
    const int FftSize = 1024;

    public static double[] ComputeBands(ReadOnlySpan<float> samples, int bandCount, double gain)
    {
        if (bandCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandCount));
        if (!double.IsFinite(gain) || gain < 0)
            gain = 1.0;

        var bands = new double[bandCount];
        if (samples.IsEmpty)
            return bands;

        var real = new double[FftSize];
        var imag = new double[FftSize];

        int count = Math.Min(samples.Length, FftSize);
        int start = samples.Length - count;
        int offset = FftSize - count;

        bool nonZero = false;
        for (int i = 0; i < count; i++)
        {
            float sample = samples[start + i];
            if (!float.IsFinite(sample))
                sample = 0;
            if (sample != 0)
                nonZero = true;
            double hann = count > 1 ? 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (count - 1))) : 1.0;
            real[offset + i] = sample * hann;
        }

        if (!nonZero)
            return bands;

        Transform(real, imag);

        int half = FftSize / 2;
        var magnitudes = new double[half];
        // Compensate for Hann window coherent gain (0.5x) and single-sided spectrum folding (2x).
        double scale = 4.0 / Math.Max(1, count);
        for (int k = 1; k < half; k++)
            magnitudes[k] = Math.Sqrt(real[k] * real[k] + imag[k] * imag[k]) * scale;

        // Logarithmic bin grouping across bins 1..(half - 1) so bass and upper-mids each span multiple columns.
        int maxBin = half - 1;
        double logMax = Math.Log(maxBin + 1);
        for (int b = 0; b < bandCount; b++)
        {
            int binStart = Math.Clamp((int)Math.Floor(Math.Exp(logMax * b / bandCount)), 1, maxBin);
            int binEnd = Math.Clamp((int)Math.Ceiling(Math.Exp(logMax * (b + 1) / bandCount)), binStart, maxBin);

            double sumSq = 0;
            double maxBinVal = 0;
            for (int k = binStart; k <= binEnd; k++)
            {
                double m = magnitudes[k];
                sumSq += m * m;
                if (m > maxBinVal)
                    maxBinVal = m;
            }
            double bandEnergy = Math.Sqrt(sumSq);
            double blended = 0.65 * bandEnergy + 0.35 * maxBinVal;
            // Subtle high-frequency tilt + 6.0x base scale for typical WASAPI desktop playback levels.
            double tilt = 1.0 + 0.5 * b / Math.Max(1, bandCount - 1);
            double value = Math.Sqrt(blended * 6.0 * gain * tilt);
            bands[b] = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        }

        return bands;
    }

    static void Transform(double[] real, double[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double wStepR = Math.Cos(angle);
            double wStepI = Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                double wR = 1.0;
                double wI = 0.0;
                int half = len >> 1;
                for (int j = 0; j < half; j++)
                {
                    int even = i + j;
                    int odd = even + half;
                    double tR = real[odd] * wR - imag[odd] * wI;
                    double tI = real[odd] * wI + imag[odd] * wR;

                    real[odd] = real[even] - tR;
                    imag[odd] = imag[even] - tI;
                    real[even] += tR;
                    imag[even] += tI;

                    double nextR = wR * wStepR - wI * wStepI;
                    wI = wR * wStepI + wI * wStepR;
                    wR = nextR;
                }
            }
        }
    }
}
