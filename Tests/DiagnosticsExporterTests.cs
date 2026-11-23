using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class DiagnosticsExporterTests
{
    [Fact]
    public void SanitizeLog_RedactsUserPathsHidPathsAndSerials()
    {
        string raw =
            "[12:00:01] AE6 V2 1330 CID 10 MID 36 type 1\n" +
            @"System.IO.IOException: failed at C:\Users\Alice\AppData\Local\DareuLocal\settings.json" + "\n" +
            @"Device: \\?\hid#vid_260d&pid_1330&mi_01#7&12345abc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" + "\n" +
            "SerialNumber=SN9876543210 UID A1B2C3D4E5F60718";

        string clean = DiagnosticsExporter.SanitizeLog(raw);

        Assert.DoesNotContain("Alice", clean);
        Assert.Contains(@"C:\Users\<redacted>\AppData", clean);
        Assert.DoesNotContain(@"\\?\hid#", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<hid-path-redacted>", clean);
        Assert.DoesNotContain("SN9876543210", clean);
        Assert.DoesNotContain("A1B2C3D4E5F60718", clean);
        Assert.Contains("AE6 V2 1330", clean);
    }

    [Fact]
    public void BuildReport_FormatsMarkdownWithEndpointsAndSanitizedLog()
    {
        string report = DiagnosticsExporter.BuildReport(
            "0.9.0",
            new[] { "AE6 V2 · USB 有线", "EK75 · 2.4G 接收器" },
            @"Error in C:\Users\Bob\foo.cs");

        Assert.Contains("0.9.0", report);
        Assert.Contains("AE6 V2 · USB 有线", report);
        Assert.DoesNotContain("Bob", report);
        Assert.Contains(@"C:\Users\<redacted>\foo.cs", report);
    }
}
