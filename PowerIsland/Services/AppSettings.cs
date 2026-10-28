using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PowerIsland.Services;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 60;
    public int LowThreshold { get; set; } = 20;
    public bool AutoStart { get; set; }
    public bool LowBatteryNotify { get; set; } = true;
    public bool TrayShowNumber { get; set; } = true;
    public bool ShowUnknownDevices { get; set; }
    public bool DarkTheme { get; set; }
    public bool ShowedTrayHint { get; set; }
    public double WindowWidth { get; set; } = 940;
    public double WindowHeight { get; set; } = 822;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerIsland");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // 配置损坏时回落到默认值
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 忽略写入失败
        }
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerIsland";

    public static void Apply(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                    ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length == 0) return;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 无权限时静默失败
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}
