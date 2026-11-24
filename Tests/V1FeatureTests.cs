using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class V1FeatureTests
{
    [Fact]
    public void TgKeyboard_BulkAssignPayloadAndReply_EncodeAndParseCorrectly()
    {
        var keys = new byte[] { 4, 22, 40 };
        byte[] payload = TgKeyboard.BulkAssignRequestPayload(keys, layer: 1);

        Assert.Equal(8 * keys.Length + 1, payload.Length);
        Assert.Equal(keys.Length, payload[0]);
        for (int i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i], payload[1 + i * 8]);
            Assert.Equal((byte)1, payload[1 + i * 8 + 1]);
        }

        Assert.ThrowsAny<ArgumentException>(() => TgKeyboard.BulkAssignRequestPayload(Array.Empty<byte>(), 0));
        Assert.ThrowsAny<ArgumentException>(() => TgKeyboard.BulkAssignRequestPayload(new byte[8], 0));

        var reply = new byte[64];
        reply[6 + 0 * 8 + 3] = 6;
        reply[6 + 0 * 8 + 4] = 0x01;
        reply[6 + 0 * 8 + 5] = 0x1E;
        reply[6 + 1 * 8 + 3] = 12;
        reply[6 + 1 * 8 + 4] = 3;
        reply[6 + 1 * 8 + 5] = 2;

        var parsed = TgKeyboard.ParseBulkAssignReply(reply, 2);
        Assert.Equal(2, parsed.Length);
        Assert.Equal((byte)6, parsed[0].Function);
        Assert.Equal(new byte[] { 0x01, 0x1E, 0, 0, 0 }, parsed[0].Data);
        Assert.Equal((byte)12, parsed[1].Function);
        Assert.Equal(new byte[] { 3, 2, 0, 0, 0 }, parsed[1].Data);
    }

    [Fact]
    public void JmMouse_TurboAndSniperDpiEncoders_RoundTripAndValidateBounds()
    {
        Assert.Equal(new byte[] { 4, 50, 0 }, JmMouse.EncodeTurboButton(50));
        Assert.ThrowsAny<ArgumentException>(() => JmMouse.EncodeTurboButton(9));
        Assert.ThrowsAny<ArgumentException>(() => JmMouse.EncodeTurboButton(256));

        foreach (int dpi in new[] { 100, 400, 800, 1600, 12800 })
        {
            byte[] encoded = JmMouse.EncodeSniperDpi(dpi);
            Assert.Equal((byte)10, encoded[0]);
            Assert.Equal((byte)0, encoded[2]);
            Assert.Equal(dpi, JmMouse.DecodeSniperDpi(encoded[1]));
        }

        Assert.ThrowsAny<ArgumentException>(() => JmMouse.EncodeSniperDpi(50));
        Assert.ThrowsAny<ArgumentException>(() => JmMouse.EncodeSniperDpi(12850));
        Assert.ThrowsAny<ArgumentException>(() => JmMouse.EncodeSniperDpi(425));
    }

    [Fact]
    public void RgbPresets_AllPresetsProduceExpectedMatrixDimensionsAndLitPixels()
    {
        Assert.Equal(5, RgbPresets.All.Length);
        foreach (var preset in RgbPresets.All)
        {
            var matrix = RgbPresets.Generate(6, 21, preset.Id);
            Assert.Equal(6, matrix.Rows);
            Assert.Equal(21, matrix.Columns);
            Assert.Equal(6 * 21 * 3, matrix.Colors.Length);
            Assert.Contains(matrix.Colors, c => c > 0);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => RgbPresets.Generate(6, 21, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RgbPresets.Generate(6, 21, 5));
    }

    [Fact]
    public void AudioFramePainter_Mode4_IsHorizontallySymmetric()
    {
        var color = new byte[] { 0x40, 0xC0, 0xFF };
        var bands = new[] { 1.0, 0.75, 0.4, 0.1 };
        int rows = 6;
        int cols = 12;

        var matrix = AudioFramePainter.PaintSpectrum(rows, cols, bands, mode: 4, color, brightness: 1.0);
        Assert.Equal(rows * cols * 3, matrix.Colors.Length);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols / 2; c++)
            {
                int leftIdx = (r * cols + c) * 3;
                int rightIdx = (r * cols + (cols - 1 - c)) * 3;
                Assert.Equal(matrix.Colors[leftIdx], matrix.Colors[rightIdx]);
                Assert.Equal(matrix.Colors[leftIdx + 1], matrix.Colors[rightIdx + 1]);
                Assert.Equal(matrix.Colors[leftIdx + 2], matrix.Colors[rightIdx + 2]);
            }
        }

        // Center column at bottom row should be lit while outer edge top row is dark
        int centerBottom = ((rows - 1) * cols + (cols / 2)) * 3;
        int outerTop = (0 * cols + 0) * 3;
        Assert.True(matrix.Colors[centerBottom] + matrix.Colors[centerBottom + 1] + matrix.Colors[centerBottom + 2] > 0);
        Assert.Equal(0, matrix.Colors[outerTop] + matrix.Colors[outerTop + 1] + matrix.Colors[outerTop + 2]);
    }
}
