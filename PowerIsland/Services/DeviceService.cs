using System.Collections.ObjectModel;
using PowerIsland.Models;

namespace PowerIsland.Services;

public sealed class DeviceService
{
    public ObservableCollection<Peripheral> Devices { get; } = new();

    /// <summary>最近一次扫描到的全部候选（含被「我的设备」过滤掉的），供「添加外设」对话框使用。</summary>
    public IReadOnlyList<Peripheral> AllScanned { get; private set; } = Array.Empty<Peripheral>();

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

            List<Peripheral> first = await Task.Run(() => Build(hid, new List<BluetoothDeviceInfo>(), settings));
            AllScanned = first;
            Merge(first.Where(p => ShouldShow(p, settings)).ToList());
            LastUpdated = DateTime.Now;
            Log.Write($"refresh timing: enumerate={enumerateMs}ms total={clock.ElapsedMilliseconds}ms shown={Devices.Count}");
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
            AllScanned = withBt;
            Merge(withBt.Where(p => ShouldShow(p, settings)).ToList());
            LastUpdated = DateTime.Now;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Busy = false;
        }
    }

    private static List<Peripheral> Build(List<HidInterface> hid, List<BluetoothDeviceInfo> bt, AppSettings settings)
    {
        var result = new List<Peripheral>();
        var claimed = new HashSet<(ushort, ushort)>();

        foreach (KnownDevice known in KnownDevices.Distinct())
        {
            HidInterface? wireless = hid.FirstOrDefault(h => h.Vid == known.Vid && h.Pid == known.PidWireless
                                                             && h.UsagePage == known.UsagePage && h.Usage == known.Usage);
            HidInterface? wired = hid.FirstOrDefault(h => h.Vid == known.Vid && h.Pid == known.PidWired
                                                          && h.UsagePage == known.UsagePage && h.Usage == known.Usage);
            bool present = wireless != null || wired != null;
            claimed.Add((known.Vid, known.PidWireless));
            claimed.Add((known.Vid, known.PidWired));

            HidInterface? source = wireless ?? wired;
            BatteryReading? reading = present ? CompxReader.Query(source!, known.Protocol) : null;

            result.Add(new Peripheral
            {
                Id = $"{known.Vid:X4}:{known.PidWireless:X4}",
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
            claimed.Add((RazerReader.RazerVid, razer.PidWireless));
            claimed.Add((RazerReader.RazerVid, razer.PidWired));

            bool wireless = hasWireless;
            BatteryReading? razerReading = present ? RazerReader.Query(razer, hid, wireless) : null;

            result.Add(new Peripheral
            {
                Id = $"{RazerReader.RazerVid:X4}:{razer.PidWireless:X4}",
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

        // 3) 型号表之外的设备：试标准 HID 电量（Usage 0x06/0x20），读到就当成一台可用设备
        foreach (var group in hid.Where(h => !claimed.Contains((h.Vid, h.Pid)))
                                 .GroupBy(h => (h.Vid, h.Pid)))
        {
            if (group.Key.Vid == RazerReader.RazerVid) continue;   // 雷蛇已单独处理，别重复试

            // 每个型号最多试两个接口，优先报告长度大的（更有可能是电量通道）
            foreach (HidInterface iface in group.OrderByDescending(h => h.FeatureLength + h.InputLength).Take(2))
            {
                BatteryReading? reading = GenericHidBattery.TryRead(iface);
                if (reading == null) continue;

                string name = string.IsNullOrWhiteSpace(iface.Product)
                    ? $"外设 {group.Key.Vid:X4}:{group.Key.Pid:X4}"
                    : iface.Product;

                result.Add(new Peripheral
                {
                    Id = $"hid:{group.Key.Vid:X4}:{group.Key.Pid:X4}",
                    Name = name,
                    Meta = $"{group.Key.Vid:X4}:{group.Key.Pid:X4} · 标准 HID 电量",
                    Vendor = string.IsNullOrWhiteSpace(iface.Manufacturer) ? "其他" : iface.Manufacturer,
                    Kind = GuessKind(name),
                    Link = group.Any(h => h.UsagePage == 0x0001) ? LinkKind.Wireless24 : LinkKind.Unknown,
                    Battery = reading.Value.Level,
                    Online = true,
                    BatteryReadable = true,
                    Vid = group.Key.Vid,
                    Pid = group.Key.Pid
                });
                break;
            }
        }

        // 4) 蓝牙：Windows 自己上报的电量（选中模式下只保留用户添加过的）
        foreach (BluetoothDeviceInfo info in bt)
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

        // 5) 剩下连电量都读不到的设备，只在用户手动打开开关时列出来
        if (settings.ShowUnknownDevices)
        {
            foreach (var group in hid.Where(h => !string.IsNullOrWhiteSpace(h.Product))
                                      .GroupBy(h => (h.Vid, h.Pid, h.Product)))
            {
                if (claimed.Contains((group.Key.Vid, group.Key.Pid))) continue;
                if (result.Any(r => r.Vid == group.Key.Vid && r.Pid == group.Key.Pid)) continue;

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

    /// <summary>自动模式只显示在线的；手动添加过设备则只显示选中的（没插也留着显示未连接）。</summary>
    private static bool ShouldShow(Peripheral device, AppSettings settings)
        => settings.MyDevices.Count > 0 ? settings.MyDevices.Contains(device.Id) : device.Online;

    private static DeviceKind GuessKind(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("鼠标")) return DeviceKind.Mouse;
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
