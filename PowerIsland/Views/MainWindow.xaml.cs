using RadioButton = System.Windows.Controls.RadioButton;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PowerIsland.Models;
using PowerIsland.Services;

namespace PowerIsland.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _retryTimer = new();
    private readonly Dictionary<string, bool> _lowNotified = new();
    private bool _refreshing;
    private bool _exiting;
    private string _statusSummary = "正在扫描…";
    private string _lastUpdatedText = "";
    private bool _hasNoDevices;
    private string _themeGlyph = "\uE708";

    public ObservableCollection<Peripheral> Devices => App.Devices.Devices;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null) VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        Loaded += OnLoaded;
        App.Devices.Updated += (_, _) => UpdateStatus();
    }

    public string StatusSummary
    {
        get => _statusSummary;
        private set { _statusSummary = value; Raise(); }
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set { _lastUpdatedText = value; Raise(); }
    }

    public bool HasNoDevices
    {
        get => _hasNoDevices;
        private set { _hasNoDevices = value; Raise(); }
    }

    public string ThemeGlyph
    {
        get => _themeGlyph;
        private set { _themeGlyph = value; Raise(); }
    }

    public void BeginWatching()
    {
        SyncSettingsToUi();
        ApplyTimer();

        // 设备休眠或首次读取失败时，快速补刷，不必等下一个刷新周期
        _retryTimer.Interval = TimeSpan.FromSeconds(6);
        _retryTimer.Tick += (_, _) =>
        {
            if (_refreshing) return;
            if (Devices.Count == 0 || Devices.Any(d => d.Online && !d.Battery.HasValue)) _ = RefreshAsync();
        };
        _retryTimer.Start();

        _ = RefreshAsync();
    }

    // ================= 圆角窗口（Win11 DWM） =================
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        }
        catch
        {
            // 旧系统不支持时保持直角
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowPlacement();
        SyncSettingsToUi();
    }

    private void RestoreWindowPlacement()
    {
        AppSettings s = App.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowLeft is double left && s.WindowTop is double top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    private static bool IsOnScreen(double left, double top)
    {
        return left > -2000 && top > -2000 && left < 20000 && top < 20000;
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        if (Devices.Count == 0) StatusSummary = "正在读取电量…";
        try
        {
            await App.Devices.RefreshAsync(App.Settings);
            UpdateStatus();
            CheckLowBattery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusSummary = "读取失败，稍后重试";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateStatus()
    {
        int total = Devices.Count;
        int online = Devices.Count(d => d.Online);
        DateTime stamp = App.Devices.LastUpdated;

        StatusSummary = total == 0
            ? "未检测到外设"
            : $"{online}/{total} 台设备在线 · 更新于 {stamp:HH:mm:ss}";
        double age = (DateTime.Now - stamp).TotalSeconds;
        LastUpdatedText = $"上次更新 {stamp:HH:mm:ss} · " +
                          (age < 60 ? "刚刚" : $"{(int)(age / 60)} 分钟前");
        HasNoDevices = total == 0;
    }

    private void CheckLowBattery()
    {
        foreach (Peripheral device in Devices.Where(d => d.Battery.HasValue))
        {
            bool low = device.Battery!.Value <= App.Settings.LowThreshold;
            bool already = _lowNotified.TryGetValue(device.Id, out bool flagged) && flagged;

            if (low && !already)
            {
                if (App.Settings.LowBatteryNotify) App.Tray?.ShowLowBattery(device.Name, device.Battery.Value);
                _lowNotified[device.Id] = true;
            }
            else if (!low && already)
            {
                _lowNotified[device.Id] = false;
            }
        }
    }

    private void ApplyTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(10, App.Settings.RefreshSeconds));
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
    }

    private void SyncSettingsToUi()
    {
        AppSettings s = App.Settings;
        Refresh15.IsChecked = s.RefreshSeconds <= 15;
        Refresh30.IsChecked = s.RefreshSeconds == 30;
        Refresh60.IsChecked = s.RefreshSeconds == 60;
        Refresh300.IsChecked = s.RefreshSeconds >= 300;

        Low10.IsChecked = s.LowThreshold <= 10;
        Low20.IsChecked = s.LowThreshold == 20;
        Low30.IsChecked = s.LowThreshold >= 30;

        AutoStartOn.IsChecked = s.AutoStart;
        AutoStartOff.IsChecked = !s.AutoStart;

        NotifyOn.IsChecked = s.LowBatteryNotify;
        NotifyOff.IsChecked = !s.LowBatteryNotify;

        TrayNumOn.IsChecked = s.TrayShowNumber;
        TrayNumOff.IsChecked = !s.TrayShowNumber;

        UnknownOn.IsChecked = s.ShowUnknownDevices;
        UnknownOff.IsChecked = !s.ShowUnknownDevices;

        ThemeGlyph = s.DarkTheme ? "\uE706" : "\uE708";
    }

    // ================= 交互 =================
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { /* 忽略拖动异常 */ }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void OnAddDevice(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDeviceDialog(App.Settings.MyDevices) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            App.Settings.MyDevices = dialog.SelectedIds;
            App.Settings.Save();
            SyncSettingsToUi();
            _ = RefreshAsync();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnHideToTray(object sender, RoutedEventArgs e) => HideToTray();

    private void HideToTray()
    {
        SaveBounds();
        Hide();
        if (!App.Settings.ShowedTrayHint)
        {
            App.Settings.ShowedTrayHint = true;
            App.Settings.Save();
            App.Tray?.ShowInfo("电量岛还在运行", "鼠标移到托盘图标上就能看到实时电量。");
        }
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        App.Settings.DarkTheme = !App.Settings.DarkTheme;
        App.Settings.Save();
        ThemeManager.Apply(App.Settings.DarkTheme);
        ThemeGlyph = App.Settings.DarkTheme ? "\uE706" : "\uE708";
    }

    private void OnRefreshInterval(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int seconds)) return;
        App.Settings.RefreshSeconds = seconds;
        App.Settings.Save();
        ApplyTimer();
    }

    private void OnLowThreshold(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int value)) return;
        App.Settings.LowThreshold = value;
        App.Settings.Save();
        App.Tray?.RefreshVisuals();
    }

    private void OnAutoStart(object sender, RoutedEventArgs e)
    {
        App.Settings.AutoStart = AutoStartOn.IsChecked == true;
        App.Settings.Save();
        AutoStart.Apply(App.Settings.AutoStart);
        App.Tray?.RefreshVisuals();
    }

    private void OnLowNotify(object sender, RoutedEventArgs e)
    {
        App.Settings.LowBatteryNotify = NotifyOn.IsChecked == true;
        App.Settings.Save();
    }

    private void OnTrayNumber(object sender, RoutedEventArgs e)
    {
        App.Settings.TrayShowNumber = TrayNumOn.IsChecked == true;
        App.Settings.Save();
        App.Tray?.RefreshVisuals();
    }

    private void OnShowUnknown(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowUnknownDevices = UnknownOn.IsChecked == true;
        App.Settings.Save();
        _ = RefreshAsync();
    }

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppSettings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", AppSettings.Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    // ================= 生命周期 =================
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true;
        Topmost = false;
        Activate();
        Focus();
    }

    public void PrepareExit() => _exiting = true;

    private void SaveBounds()
    {
        AppSettings s = App.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
        }
        s.WindowWidth = Width;
        s.WindowHeight = Height;
        s.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SaveBounds();
        _timer.Stop();
        base.OnClosing(e);
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
