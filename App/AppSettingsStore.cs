using System.Text.Json;
using DareuLocal.Core;
using Microsoft.Win32;

namespace DareuLocal.App;

internal sealed class AppSettingsStore
{
    const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "DareuLocal";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    readonly string filePath;

    public AppSettingsStore(string folder)
    {
        filePath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(filePath))
                return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions) ?? new AppSettings();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, filePath, overwrite: true);
    }

    public static void ApplyStartWithWindows(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
        if (key == null)
            throw new IOException("无法打开 Windows 启动项注册表路径。");

        if (!enabled)
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return;
        }

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new IOException("无法定位当前可执行文件路径。");

        string command = $"\"{exe}\"" + (startMinimized ? " --minimized" : "");
        key.SetValue(RunValueName, command, RegistryValueKind.String);
    }
}
