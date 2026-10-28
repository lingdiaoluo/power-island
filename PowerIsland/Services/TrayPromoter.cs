using Microsoft.Win32;

namespace PowerIsland.Services;

/// <summary>
/// Windows 11 默认把新应用的托盘图标放进“隐藏的图标”折叠区。
/// 这里按系统自己的规则（NotifyIconSettings\IsPromoted）请求常显，用户随时可在任务栏设置里改回来。
/// </summary>
public static class TrayPromoter
{
    private const string Root = @"Control Panel\NotifyIconSettings";

    public static void TryPromote(Action? afterPromote = null)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(Root, writable: true);
            if (root == null) return;

            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? item = root.OpenSubKey(name, writable: true);
                if (item == null) continue;
                if (item.GetValue("ExecutablePath") is not string path) continue;
                if (!string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) continue;

                if (item.GetValue("IsPromoted") is int promoted && promoted == 1) return;
                item.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                Log.Write($"tray icon promoted: {exe}");
                afterPromote?.Invoke();
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Write("tray promote failed: " + ex.Message);
        }
    }
}
