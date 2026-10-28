using System.IO;
using System.Threading;
using System.Windows;
using PowerIsland.Services;
using PowerIsland.Views;

namespace PowerIsland;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static DeviceService Devices { get; private set; } = new();
    public static TrayService? Tray { get; private set; }

    private Mutex? _mutex;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(true, "PowerIsland.SingleInstance.v1", out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) => Log(args.Exception.ToString());
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject?.ToString() ?? "unknown");

            Settings = AppSettings.Load();
            ThemeManager.Apply(Settings.DarkTheme);

            base.OnStartup(e);

            Devices = new DeviceService();
            _window = new MainWindow();
            MainWindow = _window;

            Tray = new TrayService(Devices, Settings,
                showWindow: () => _window.ShowFromTray(),
                refresh: () => _ = _window.RefreshAsync(),
                exit: ExitApp);

            bool startHidden = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            if (!startHidden) _window.Show();

            _window.BeginWatching();

            // 让托盘图标进入可见区域（系统会在下一次刷新时应用）
            var promoteTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            promoteTimer.Tick += (_, _) =>
            {
                promoteTimer.Stop();
                TrayPromoter.TryPromote(() => Tray?.RefreshRegistration());
            };
            promoteTimer.Start();
        }
        catch (Exception ex)
        {
            Log("STARTUP FAILED: " + ex);
            System.Windows.MessageBox.Show(ex.ToString(), "电量岛启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    public void ExitApp()
    {
        _window?.PrepareExit();
        Shutdown();
    }

    private static void Log(string text) => Services.Log.Write(text);
}

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };

        ResourceDictionary resources = System.Windows.Application.Current.Resources;
        if (resources.MergedDictionaries.Count == 0)
        {
            resources.MergedDictionaries.Add(dict);
        }
        else
        {
            resources.MergedDictionaries[0] = dict;
        }
    }
}
