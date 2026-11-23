using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class JmMouseWatcherTests
{
    [Fact]
    public void IsHardwareEventPacket_AcceptsCapturedAe6DpiReport()
    {
        byte[] packet = Convert.FromHexString("080A0000000A0100000000000000000038");
        Assert.True(JmMouseWatcher.IsHardwareEventPacket(packet));
    }

    [Fact]
    public void IsHardwareEventPacket_RejectsNormalCommandReplies()
    {
        byte[] stateReply = JmMouse.Packet(8, 0, 10);
        byte[] batteryReply = JmMouse.Packet(4, 0, 0, new byte[] { 85, 0 });
        Assert.False(JmMouseWatcher.IsHardwareEventPacket(stateReply));
        Assert.False(JmMouseWatcher.IsHardwareEventPacket(batteryReply));
    }

    [Fact]
    public void IsHardwareEventPacket_RejectsInvalidLengthReportIdOrChecksum()
    {
        byte[] valid = Convert.FromHexString("080A0000000A0100000000000000000038");

        Assert.False(JmMouseWatcher.IsHardwareEventPacket(valid.AsSpan(0, 16)));

        byte[] wrongReportId = (byte[])valid.Clone();
        wrongReportId[0] = 0x07;
        wrongReportId[16] = JmMouse.Checksum(wrongReportId.AsSpan(0, 16));
        Assert.False(JmMouseWatcher.IsHardwareEventPacket(wrongReportId));

        byte[] badChecksum = (byte[])valid.Clone();
        badChecksum[16] ^= 0x01;
        Assert.False(JmMouseWatcher.IsHardwareEventPacket(badChecksum));
    }
}
