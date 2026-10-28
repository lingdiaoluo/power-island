using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace PowerIsland.Services;

public sealed record BluetoothDeviceInfo(string Name, int Level);

/// <summary>
/// 蓝牙 / 蓝牙 LE 外设的电量：直接读 Windows 自己在“设置 - 蓝牙”里显示的关联端点属性。
/// </summary>
public static class BluetoothBattery
{
    public static async Task<List<BluetoothDeviceInfo>> GetAsync()
    {
        var found = new List<BluetoothDeviceInfo>();
        var selectors = new List<string>();
        try { selectors.Add(BluetoothDevice.GetDeviceSelectorFromPairingState(true)); } catch { }
        try { selectors.Add(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)); } catch { }

        foreach (string selector in selectors)
        {
            try
            {
                var properties = new[]
                {
                    "System.Devices.BatteryLife",
                    "System.Devices.Aep.IsConnected"
                };
                var devices = await DeviceInformation.FindAllAsync(selector, properties,
                    DeviceInformationKind.AssociationEndpoint);

                foreach (var info in devices)
                {
                    if (string.IsNullOrWhiteSpace(info.Name)) continue;

                    bool connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out object? c)
                                     && c is bool b && b;
                    if (!connected) continue;

                    if (!info.Properties.TryGetValue("System.Devices.BatteryLife", out object? raw)) continue;
                    if (raw is not byte level || level > 100) continue;

                    if (found.Any(f => f.Name == info.Name)) continue;
                    found.Add(new BluetoothDeviceInfo(info.Name, level));
                }
            }
            catch
            {
                // 没有蓝牙适配器 / 无权限时静默跳过
            }
        }

        return found;
    }
}
