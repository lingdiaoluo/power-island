using System.Drawing;
using System.Linq;
using System.Windows.Threading;
using PowerIsland.Models;
using PowerIsland.Views;
using WinForms = System.Windows.Forms;

namespace PowerIsland.Services;

/// <summary>右下角通知区域：动态电量图标 + 悬停电量气泡 + 右键菜单。</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly TrayPopup _popup;
    private readonly DeviceService _devices;
    private readonly AppSettings _settings;
    private readonly Action _showWindow;
    private readonly Action _refresh;
    private readonly Action _exit;
    private readonly WinForms.ToolStripMenuItem _numberItem;
    private readonly WinForms.ToolStripMenuItem _autoStartItem;
    private readonly Queue<Icon> _iconHistory = new();
    private readonly DispatcherTimer _hoverTimer;
    private System.Drawing.Rectangle? _iconRect;
    private DateTime _rectStamp = DateTime.MinValue;
    private DateTime? _hoverSince;
    private bool _disposed;

    public TrayService(DeviceService devices, AppSettings settings,
        Action showWindow, Action refresh, Action exit)
    {
        _devices = devices;
        _settings = settings;
        _showWindow = showWindow;
        _refresh = refresh;
        _exit = exit;

        _popup = new TrayPopup { DataContext = _devices.Devices };

        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false, Font = new Font("Microsoft YaHei UI", 9f) };

        var openItem = new WinForms.ToolStripMenuItem("打开 电量岛") { Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
        openItem.Click += (_, _) => _showWindow();
        menu.Items.Add(openItem);

        var refreshItem = new WinForms.ToolStripMenuItem("立即刷新");
        refreshItem.Click += (_, _) => _refresh();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        _numberItem = new WinForms.ToolStripMenuItem("托盘显示电量数字")
        {
            CheckOnClick = true,
            Checked = settings.TrayShowNumber
        };
        _numberItem.CheckedChanged += (_, _) =>
        {
            _settings.TrayShowNumber = _numberItem.Checked;
            _settings.Save();
            RefreshVisuals();
        };
        menu.Items.Add(_numberItem);

        _autoStartItem = new WinForms.ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled()
        };
        _autoStartItem.CheckedChanged += (_, _) =>
        {
            _settings.AutoStart = _autoStartItem.Checked;
            _settings.Save();
            AutoStart.Apply(_autoStartItem.Checked);
        };
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => _exit();
        menu.Items.Add(exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu,
            Text = "电量岛"
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button != WinForms.MouseButtons.Left) return;
            _popup.HidePopup();
            _showWindow();
        };

        // Win11 不转发托盘悬停消息，改为自己轮询光标位置
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _hoverTimer.Tick += (_, _) => PollHover();
        _hoverTimer.Start();

        _devices.Updated += (_, _) => RefreshVisuals();
        RefreshVisuals();
    }

    /// <summary>重新注册图标，让系统立即应用“常显”设置。</summary>
    public void RefreshRegistration()
    {
        try
        {
            _icon.Visible = false;
            _icon.Visible = true;
            RefreshVisuals();
        }
        catch (Exception ex)
        {
            Log.Write("tray re-register failed: " + ex.Message);
        }
    }

    public void RefreshVisuals()
    {
        if (_disposed) return;

        int? level = _devices.Devices.Where(d => d.Battery.HasValue).Select(d => d.Battery)
            .DefaultIfEmpty(null).Min();

        Icon icon = TrayIconFactory.Create(level, _settings.TrayShowNumber, _settings.LowThreshold);
        _icon.Icon = icon;
        _iconHistory.Enqueue(icon);
        while (_iconHistory.Count > 3) _iconHistory.Dequeue().Dispose();

        _numberItem.Checked = _settings.TrayShowNumber;
        _autoStartItem.Checked = AutoStart.IsEnabled();

        try
        {
            int count = _devices.Devices.Count;
            _icon.Text = count == 0 ? "电量岛 · 未检测到外设" : $"电量岛 · {count} 台设备";
        }
        catch
        {
            _icon.Text = "电量岛";
        }
    }

    private void PollHover()
    {
        if (_disposed) return;
        try
        {
            System.Drawing.Point cursor = TrayHitTest.CursorPosition();

            if (_iconRect == null || (DateTime.UtcNow - _rectStamp).TotalSeconds > 2)
            {
                _iconRect = TrayHitTest.GetIconRect(_icon);
                _rectStamp = DateTime.UtcNow;
                if (_iconRect == null) Log.Write("icon rect unavailable");
            }

            bool overIcon = _iconRect.HasValue &&
                            cursor.X >= _iconRect.Value.Left - 2 && cursor.X <= _iconRect.Value.Right + 2 &&
                            cursor.Y >= _iconRect.Value.Top - 2 && cursor.Y <= _iconRect.Value.Bottom + 2;

            if (overIcon)
            {
                _hoverSince ??= DateTime.UtcNow;
                if (!_popup.IsVisible && _devices.Devices.Count > 0 &&
                    (DateTime.UtcNow - _hoverSince.Value).TotalMilliseconds >= 380)
                {
                    _popup.DataContext = _devices.Devices;
                    _popup.ShowNear(cursor);
                }
                return;
            }

            _hoverSince = null;
            if (_popup.IsVisible && !_popup.ContainsCursor()) _popup.HidePopup();
        }
        catch (Exception ex)
        {
            Log.Write("hover poll: " + ex);
        }
    }

    private string BuildTooltip()
    {
        var rows = _devices.Devices.Take(4)
            .Select(d => $"{d.KindLabel} {Shorten(d.Name)}  {d.BatteryText}")
            .ToList();
        return rows.Count == 0 ? "电量岛 · 未检测到外设" : string.Join("\n", rows);
    }

    private static string Shorten(string name)
    {
        string trimmed = name.Replace("VGN ", "").Replace(" Dragonfly", "");
        return trimmed.Length > 12 ? trimmed[..12] : trimmed;
    }

    public void ShowLowBattery(string device, int level)
    {
        try
        {
            _icon.ShowBalloonTip(4000, "电量偏低", $"{device} 剩余 {level}%，记得充电。",
                WinForms.ToolTipIcon.Warning);
        }
        catch
        {
            // 通知不可用时忽略
        }
    }

    public void ShowInfo(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(3500, title, text, WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // 通知不可用时忽略
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hoverTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _popup.Close();
        foreach (Icon icon in _iconHistory) icon.Dispose();
        _iconHistory.Clear();
    }
}
