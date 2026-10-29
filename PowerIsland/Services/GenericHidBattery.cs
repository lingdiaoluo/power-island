using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PowerIsland.Interop;

namespace PowerIsland.Services;

/// <summary>
/// 标准 HID 电量：Usage Page 0x06（Generic Device Controls）/ Usage 0x20（Battery Strength）。
/// 只有固件真的实现了这个标准用法的设备才读得到；厂商私有协议走各自的 Reader。
/// </summary>
public static class GenericHidBattery
{
    private const ushort PageGenericDevice = 0x06;
    private const ushort UsageBatteryStrength = 0x20;

    /// <summary>先试特征报告，再试输入报告；读不到就返回 null，不改动设备状态。</summary>
    public static BatteryReading? TryRead(HidInterface iface, int timeoutMs = 250)
    {
        if (iface.FeatureLength < 2 && iface.InputLength < 2) return null;

        BatteryReading? result = null;
        var worker = new Thread(() => result = TryReadCore(iface))
        {
            IsBackground = true,
            Name = "hid-generic-battery"
        };
        worker.Start();

        // 个别固件会在读特征报告时卡住，超时就直接放弃这一台
        return worker.Join(timeoutMs) ? result : null;
    }

    private static BatteryReading? TryReadCore(HidInterface iface)
    {
        SafeFileHandle handle;
        try
        {
            handle = HidNative.OpenDevice(iface.Path, true);
        }
        catch
        {
            return null;
        }

        if (handle.IsInvalid) return null;

        try
        {
            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
            try
            {
                if (iface.FeatureLength >= 2)
                {
                    var buffer = new byte[iface.FeatureLength];
                    for (int id = 0; id <= 4; id++)
                    {
                        buffer[0] = (byte)id;
                        if (!HidNative.HidD_GetFeature(handle, buffer, buffer.Length)) continue;
                        if (TryParse(preparsed, buffer, HidNative.HidP_Feature, out int level))
                            return new BatteryReading(level, false);
                    }
                }

                if (iface.InputLength >= 2)
                {
                    var buffer = new byte[iface.InputLength];
                    for (int id = 0; id <= 4; id++)
                    {
                        buffer[0] = (byte)id;
                        if (!HidNative.HidD_GetInputReport(handle, buffer, buffer.Length)) continue;
                        if (TryParse(preparsed, buffer, HidNative.HidP_Input, out int level))
                            return new BatteryReading(level, false);
                    }
                }

                return null;
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static bool TryParse(IntPtr preparsed, byte[] report, int reportType, out int level)
    {
        level = 0;
        int status = HidNative.HidP_GetUsageValue(reportType, PageGenericDevice, 0, UsageBatteryStrength,
            out uint value, preparsed, report, report.Length);
        if (status != HidNative.HIDP_STATUS_SUCCESS) return false;
        if (value > 100) return false;   // 正常电量是 0-100，超范围说明解析错了

        level = (int)value;
        return true;
    }
}
