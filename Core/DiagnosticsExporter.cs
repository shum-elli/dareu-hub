using System.Text;
using System.Text.RegularExpressions;

namespace DareuLocal.Core;

/// <summary>
/// Redacts local user paths, HID device paths, and serial tokens before exporting diagnostics.
/// </summary>
public static partial class DiagnosticsExporter
{
    [GeneratedRegex(@"([A-Za-z]:\\Users\\)([^\\/\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"(/(?:Users|home)/)([^/\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnixUserPathRegex();

    [GeneratedRegex(@"\\\\\?\\hid#[^\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex HidPathRegex();

    [GeneratedRegex(@"(?i)(serial(?:number)?\s*[:=]\s*|SN\s*[:=]\s*)([A-Za-z0-9_-]{4,})")]
    private static partial Regex SerialFieldRegex();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{16,32}\b")]
    private static partial Regex LongHexTokenRegex();

    public static string SanitizeLog(string? rawLog)
    {
        if (string.IsNullOrEmpty(rawLog))
            return "";

        string sanitized = HidPathRegex().Replace(rawLog, "<hid-path-redacted>");
        sanitized = WindowsUserPathRegex().Replace(sanitized, "$1<redacted>");
        sanitized = UnixUserPathRegex().Replace(sanitized, "$1<redacted>");
        sanitized = SerialFieldRegex().Replace(sanitized, "$1<redacted>");
        sanitized = LongHexTokenRegex().Replace(sanitized, "<hex-redacted>");

        string user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length >= 2)
            sanitized = sanitized.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);

        string machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine) && machine.Length >= 2)
            sanitized = sanitized.Replace(machine, "<machine>", StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }

    public static string BuildReport(string appVersion, IEnumerable<string> connectedLabels, string? rawLog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dareu Local 脱敏诊断报告");
        sb.AppendLine();
        sb.AppendLine($"- 应用版本：{appVersion}");
        sb.AppendLine($"- 操作系统：{Environment.OSVersion}");
        sb.AppendLine($"- .NET 运行时：{Environment.Version}");
        sb.AppendLine($"- 生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine("## 已连接设备");
        var labels = connectedLabels?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
        if (labels.Count == 0)
            sb.AppendLine("- （未检测到设备）");
        else
            foreach (var label in labels)
                sb.AppendLine($"- {SanitizeLog(label)}");
        sb.AppendLine();
        sb.AppendLine("## 通信诊断记录（已脱敏）");
        sb.AppendLine("```text");
        string cleanLog = SanitizeLog(rawLog).Trim();
        sb.AppendLine(cleanLog.Length == 0 ? "（暂无记录）" : cleanLog);
        sb.AppendLine("```");
        return sb.ToString();
    }
}
