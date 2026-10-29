using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PowerIsland.Models;

public enum DeviceKind
{
    Mouse,
    Keyboard,
    Gamepad,
    Headset,
    Other
}

public enum LinkKind
{
    Wireless24,
    Bluetooth,
    Wired,
    Unknown
}

public sealed class Peripheral : INotifyPropertyChanged
{
    private int? _battery;
    private bool _online = true;
    private bool _charging;
    private bool _batteryReadable = true;

    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Vendor { get; set; } = "";
    public DeviceKind Kind { get; set; } = DeviceKind.Other;
    public LinkKind Link { get; set; } = LinkKind.Unknown;
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public bool BatteryReadable
    {
        get => _batteryReadable;
        set
        {
            if (_batteryReadable == value) return;
            _batteryReadable = value;
            Raise();
            Raise(nameof(BatteryHintText));
            Raise(nameof(ShowBatteryHint));
        }
    }

    public int? Battery
    {
        get => _battery;
        set
        {
            if (_battery == value) return;
            _battery = value;
            Raise();
            Raise(nameof(BatteryText));
            Raise(nameof(HasBattery));
            Raise(nameof(Level));
            Raise(nameof(BarValue));
            Raise(nameof(ShowBatteryHint));
            Raise(nameof(BatteryHintText));
        }
    }

    public bool Online
    {
        get => _online;
        set
        {
            if (_online == value) return;
            _online = value;
            Raise();
            Raise(nameof(StatusText));
            Raise(nameof(StatusHint));
        }
    }

    public bool Charging
    {
        get => _charging;
        set { if (_charging == value) return; _charging = value; Raise(); Raise(nameof(StatusHint)); }
    }

    public bool HasBattery => Battery.HasValue;
    public string BatteryText => Battery.HasValue ? $"{Battery.Value}%" : "—";
    public double Level => Battery ?? 0;
    public double BarValue => Battery ?? 0;

    /// <summary>读不到电量时给出针对性的解释，别让"不支持"看起来像"没插好"。</summary>
    public string BatteryHintText => !BatteryReadable
        ? "这台设备没有开放电量接口，只能显示连接状态"
        : "接收器未回传电量，动一下鼠标唤醒后会自动刷新";

    public bool ShowBatteryHint => !HasBattery;

    public string KindLabel => Kind switch
    {
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Gamepad => "手柄",
        DeviceKind.Headset => "耳机",
        _ => "外设"
    };

    public string LinkLabel => Link switch
    {
        LinkKind.Wireless24 => "2.4G 无线",
        LinkKind.Bluetooth => "蓝牙",
        LinkKind.Wired => "有线",
        _ => "未知连接"
    };

    public string StatusText => !Online ? "未连接" : Charging ? "充电中" : "已连接";

    public string StatusHint => !Online
        ? "设备未连接"
        : Charging
            ? $"{LinkLabel} · 充电中"
            : LinkLabel;

    public string ArtKey => DeviceArt.KeyFor(Vid, Kind);

    public string TrayName
    {
        get
        {
            string name = Name.Length > 16 ? Name[..16] + "…" : Name;
            return $"{KindLabel} {name}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
