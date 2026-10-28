using System.Collections.ObjectModel;
using PowerIsland.Models;

namespace PowerIsland.Services;

public sealed class DeviceService
{
    public ObservableCollection<Peripheral> Devices { get; } = new();
    public DateTime LastUpdated { get; private set; } = DateTime.Now;
    public bool Busy { get; private set; }

    public event EventHandler? Updated;

    public async Task RefreshAsync(AppSettings settings)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            // 1) 先出 USB / 2.4G 外设，不蓝牙枚举拖慢首屏
            var clock = System.Diagnostics.Stopwatch.StartNew();
            List<HidInterface> hid = await Task.Run(HidInventory.Enumerate);
            long enumerateMs = clock.ElapsedMilliseconds;
            List<Peripheral> scanned = await Task.Run(() => Build(hid, new List<BluetoothDeviceInfo>(), settings));
            Log.Write($"refresh timing: enumerate={enumerateMs}ms total={clock.ElapsedMilliseconds}ms devices={scanned.Count}");
            Merge(scanned);
            LastUpdated = DateTime.Now;
            Updated?.Invoke(this, EventArgs.Empty);

            // 2) 蓝牙外设最多等 1.5 秒，没有蓝牙适配器也不会卡住
            List<BluetoothDeviceInfo> bt;
            try
            {
                bt = await BluetoothBattery.GetAsync().WaitAsync(TimeSpan.FromMilliseconds(1500));
            }
            catch
            {
                bt = new List<BluetoothDeviceInfo>();
            }

            if (bt.Count == 0) return;

            List<Peripheral> withBt = await Task.Run(() => Build(hid, bt, settings));
            Merge(withBt);
            LastUpdated = DateTime.Now;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string Describe(List<Peripheral> devices) => devices.Count == 0
        ? "no device"
        : string.Join(" | ", devices.Select(d =>
            $"{d.Name} vid={d.Vid:X4} pid={d.Pid:X4} battery={(d.Battery?.ToString() ?? "null")} online={d.Online}"));

    private static List<Peripheral> Build(List<HidInterface> hid, List<BluetoothDeviceInfo> bt, AppSettings settings)
    {
        var result = new List<Peripheral>();

        foreach (KnownDevice known in KnownDevices.Distinct())
        {
            HidInterface? wireless = hid.FirstOrDefault(h => h.Vid == known.Vid && h.Pid == known.PidWireless
                                                             && h.UsagePage == known.UsagePage && h.Usage == known.Usage);
            HidInterface? wired = hid.FirstOrDefault(h => h.Vid == known.Vid && h.Pid == known.PidWired
                                                          && h.UsagePage == known.UsagePage && h.Usage == known.Usage);
            string id = $"{known.Vid:X4}:{known.PidWireless:X4}";
            bool present = wireless != null || wired != null;
            if (!ShouldShow(settings, id, present)) continue;

            HidInterface? source = wireless ?? wired;
            BatteryReading? reading = present ? CompxReader.Query(source!, known.Protocol) : null;

            result.Add(new Peripheral
            {
                Id = id,
                Name = known.Name,
                Meta = known.Meta,
                Vendor = "VGN / VXE / ATK",
                Kind = known.Kind,
                Link = reading?.Wired == true ? LinkKind.Wired
                       : wireless == null ? LinkKind.Wired : LinkKind.Wireless24,
                Battery = reading?.Level,
                Online = present,
                BatteryReadable = true,
                Vid = known.Vid,
                Pid = known.PidWireless
            });
        }

        foreach (RazerDevice razer in RazerReader.Devices)
        {
            bool hasWireless = hid.Any(h => h.Vid == RazerReader.RazerVid && h.Pid == razer.PidWireless);
            bool hasWired = hid.Any(h => h.Vid == RazerReader.RazerVid && h.Pid == razer.PidWired);
            bool present = hasWireless || hasWired;

            string razerId = $"{RazerReader.RazerVid:X4}:{razer.PidWireless:X4}";
            if (!ShouldShow(settings, razerId, present)) continue;

            bool wireless = hasWireless;
            BatteryReading? razerReading = present ? RazerReader.Query(razer, hid, wireless) : null;

            result.Add(new Peripheral
            {
                Id = razerId,
                Name = razer.Name,
                Meta = razer.Meta,
                Vendor = "Razer",
                Kind = razer.Kind,
                Link = wireless ? LinkKind.Wireless24 : LinkKind.Wired,
                Battery = razerReading?.Level,
                Charging = razerReading?.Charging ?? false,
                Online = present,
                BatteryReadable = true,
                Vid = RazerReader.RazerVid,
                Pid = wireless ? razer.PidWireless : razer.PidWired
            });
        }

        foreach (BluetoothDeviceInfo info in settings.MyDevices.Count == 0 ? bt : new List<BluetoothDeviceInfo>())
        {
            if (result.Any(r => string.Equals(r.Name, info.Name, StringComparison.OrdinalIgnoreCase))) continue;

            result.Add(new Peripheral
            {
                Id = "bt:" + info.Name.ToLowerInvariant(),
                Name = info.Name,
                Meta = "蓝牙 · Windows 设备电量",
                Vendor = "Bluetooth",
                Kind = GuessKind(info.Name),
                Link = LinkKind.Bluetooth,
                Battery = info.Level,
                Online = true,
                BatteryReadable = true
            });
        }

        if (settings.ShowUnknownDevices)
        {
            foreach (var group in hid.Where(h => !string.IsNullOrWhiteSpace(h.Product))
                                      .GroupBy(h => (h.Vid, h.Pid, h.Product)))
            {
                bool already = result.Any(r => r.Vid == group.Key.Vid && r.Pid == group.Key.Pid);
                if (already) continue;

                var first = group.First();
                result.Add(new Peripheral
                {
                    Id = $"hid:{group.Key.Vid:X4}:{group.Key.Pid:X4}",
                    Name = group.Key.Product,
                    Meta = $"VID_{group.Key.Vid:X4} · PID_{group.Key.Pid:X4} · 未开放电量接口",
                    Vendor = first.Manufacturer,
                    Kind = GuessKind(group.Key.Product),
                    Link = group.Any(h => h.UsagePage == 0x0001) ? LinkKind.Wireless24 : LinkKind.Unknown,
                    Battery = null,
                    Online = true,
                    BatteryReadable = false,
                    Vid = group.Key.Vid,
                    Pid = group.Key.Pid
                });
            }
        }

        return result;
    }

    /// <summary>自动模式：只显示实际插着的；手动添加过：只显示选中的（没插也显示为未连接）。</summary>
    private static bool ShouldShow(AppSettings settings, string id, bool present)
        => settings.MyDevices.Count > 0 ? settings.MyDevices.Contains(id) : present;

    private static DeviceKind GuessKind(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("鼠标") || n.Contains("鼠标")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("键盘")) return DeviceKind.Keyboard;
        if (n.Contains("gamepad") || n.Contains("controller") || n.Contains("手柄") || n.Contains("xbox"))
            return DeviceKind.Gamepad;
        if (n.Contains("headset") || n.Contains("earbud") || n.Contains("headphone") || n.Contains("耳机")
            || n.Contains("airpods") || n.Contains("buds"))
            return DeviceKind.Headset;
        return DeviceKind.Other;
    }

    private void Merge(List<Peripheral> scanned)
    {
        for (int i = 0; i < scanned.Count; i++)
        {
            Peripheral fresh = scanned[i];
            Peripheral? existing = Devices.FirstOrDefault(d => d.Id == fresh.Id);
            if (existing == null)
            {
                Devices.Insert(Math.Min(i, Devices.Count), fresh);
                continue;
            }

            existing.Name = fresh.Name;
            existing.Meta = fresh.Meta;
            existing.Kind = fresh.Kind;
            existing.Link = fresh.Link;
            existing.Online = fresh.Online;
            // 单次读取失败时保留上一次的数值，不要闪成“—”
            existing.Battery = fresh.Battery ?? (fresh.Online ? existing.Battery : null);
            existing.BatteryReadable = fresh.BatteryReadable;
            existing.Charging = fresh.Charging;
        }

        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (scanned.All(s => s.Id != Devices[i].Id)) Devices.RemoveAt(i);
        }
    }
}
