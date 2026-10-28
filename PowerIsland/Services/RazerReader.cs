using System.Text;
using Microsoft.Win32.SafeHandles;
using PowerIsland.Interop;
using PowerIsland.Models;

namespace PowerIsland.Services;

public enum RazerProtocol
{
    /// <summary>经典 90 字节 Feature Report（OpenRazer 同款）。</summary>
    Classic = 0,

    /// <summary>2023 款耳机用的 64 字节 PA / PI 帧。</summary>
    PaPi = 1
}

public enum RazerScale
{
    /// <summary>设备返回 0–255，需要换算成百分比。</summary>
    Scaled255,

    /// <summary>设备直接返回 0–100。</summary>
    Direct100,

    /// <summary>≤100 当作百分比，否则按 0–255 换算。</summary>
    Auto
}

public sealed record RazerDevice(
    string Name,
    string Meta,
    ushort PidWired,
    ushort PidWireless,
    byte TransactionId,
    RazerProtocol Protocol,
    RazerScale Scale,
    DeviceKind Kind);

/// <summary>
/// 雷蛇设备的电量读取。
/// 协议参考 OpenRazer 的逆向结果：90 字节 Feature Report，
/// 命令类 0x07 + 0x80 取电量、0x84 取充电状态，第 88 字节是 CRC（前 2..87 字节异或）。
/// </summary>
public static class RazerReader
{
    public const ushort RazerVid = 0x1532;

    private const byte CommandClassBattery = 0x07;
    private const byte CommandGetBattery = 0x80;
    private const byte CommandGetCharging = 0x84;

    private static readonly HashSet<string> LoggedFailures = new();

    public static readonly IReadOnlyList<RazerDevice> Devices = new List<RazerDevice>
    {
        new("Razer Viper V2 Pro", "Focus Pro 30K · 2.4G · HyperSpeed",
            0x00A5, 0x00A6, 0x1F, RazerProtocol.Classic, RazerScale.Scaled255, DeviceKind.Mouse),

        // 2020 款：OpenRazer 也未完整支持，先按经典协议试
        new("Razer BlackShark V2 Pro", "2020 款 · TriForce 50mm · 2.4G",
            0x0528, 0x0528, 0xFF, RazerProtocol.Classic, RazerScale.Direct100, DeviceKind.Headset),

        // 2023 款：固件不再实现经典协议，改用 64 字节 PA/PI 帧
        new("Razer BlackShark V2 Pro", "2023 款 · TriForce 50mm · 2.4G / 蓝牙",
            0x0555, 0x0555, 0xFF, RazerProtocol.PaPi, RazerScale.Direct100, DeviceKind.Headset)
    };

    /// <summary>对一台设备的所有候选 HID 接口逐个尝试，返回第一个有效读数。</summary>
    public static BatteryReading? Query(RazerDevice device, IReadOnlyList<HidInterface> interfaces, bool wireless)
    {
        ushort pid = wireless ? device.PidWireless : device.PidWired;

        IEnumerable<HidInterface> candidates = device.Protocol == RazerProtocol.Classic
            ? interfaces.Where(i => i.Vid == RazerVid && i.Pid == pid && i.FeatureLength >= 90)
            : interfaces.Where(i => i.Vid == RazerVid && i.Pid == pid && i.InputLength >= 64 && i.OutputLength >= 64);

        foreach (HidInterface iface in candidates)
        {
            BatteryReading? reading = device.Protocol == RazerProtocol.Classic
                ? QueryClassic(iface, device)
                : QueryPaPi(iface, device);

            if (reading != null) return reading;
        }

        string key = $"{device.Name}#{pid:X4}";
        if (LoggedFailures.Add(key))
        {
            var hex = candidates.Select(c => $"{c.Path.Split('#').LastOrDefault()?.Split('&').FirstOrDefault()}(in{c.InputLength}/out{c.OutputLength}/feat{c.FeatureLength})");
            Log.Write($"razer: {device.Name} pid={pid:X4} 无有效读数；接口：{string.Join(", ", hex)}");
        }

        return null;
    }

    // ================= 经典 90 字节协议 =================

    private static BatteryReading? QueryClassic(HidInterface iface, RazerDevice device)
    {
        try
        {
            using SafeFileHandle handle = HidNative.OpenDevice(iface.Path, true);
            if (handle.IsInvalid)
            {
                using SafeFileHandle readOnly = HidNative.OpenDevice(iface.Path, false);
                if (readOnly.IsInvalid) return null;
                return Exchange(readOnly, iface, device);
            }

            return Exchange(handle, iface, device);
        }
        catch (Exception ex)
        {
            Log.Write("razer classic failed: " + ex.Message);
            return null;
        }
    }

    private static BatteryReading? Exchange(SafeFileHandle handle, HidInterface iface, RazerDevice device)
    {
        bool hasReportId = iface.FeatureLength >= 91;
        int bufferSize = Math.Max(hasReportId ? 91 : 90, iface.FeatureLength);

        int? level = SendAndRead(handle, device, CommandGetBattery, hasReportId, bufferSize);
        if (level == null) return null;

        int? charging = SendAndRead(handle, device, CommandGetCharging, hasReportId, bufferSize);

        int percent = device.Scale switch
        {
            RazerScale.Scaled255 => (int)Math.Round(level.Value * 100.0 / 255.0),
            RazerScale.Direct100 => level.Value,
            _ => level.Value <= 100 ? level.Value : (int)Math.Round(level.Value * 100.0 / 255.0)
        };

        if (percent is < 0 or > 100) return null;
        return new BatteryReading(percent, false, charging is > 0);
    }

    private static int? SendAndRead(SafeFileHandle handle, RazerDevice device, byte commandId,
        bool hasReportId, int bufferSize)
    {
        byte[] request = new byte[bufferSize];
        int offset = hasReportId ? 1 : 0;
        if (hasReportId) request[0] = 0x00;

        request[offset + 0] = 0x00;                 // status: 新请求
        request[offset + 1] = device.TransactionId; // 事务号
        request[offset + 2] = 0x00;                 // remaining packets
        request[offset + 3] = 0x00;
        request[offset + 4] = 0x00;                 // protocol type
        request[offset + 5] = 0x02;                 // data size
        request[offset + 6] = CommandClassBattery;
        request[offset + 7] = commandId;
        request[offset + 88] = Crc(request, offset);
        request[offset + 89] = 0x00;

        if (!HidNative.HidD_SetFeature(handle, request, request.Length))
        {
            // 少数设备只接受 Output 报告
            if (!HidNative.WriteFile(handle, request, request.Length, out _, IntPtr.Zero))
            {
                LogOnce(device, $"setfeature/write 均失败 err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                return null;
            }
        }

        var response = new byte[bufferSize];
        if (!HidNative.HidD_GetFeature(handle, response, response.Length))
        {
            LogOnce(device, "getfeature 失败 err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return null;
        }

        byte status = response[offset + 0];
        if (status != 0x02 && status != 0x01)
        {
            LogOnce(device, $"状态异常 0x{status:X2}，原始：{Convert.ToHexString(response)}");
            return null;
        }

        if (response[offset + 1] != device.TransactionId ||
            response[offset + 6] != CommandClassBattery ||
            response[offset + 7] != commandId)
        {
            LogOnce(device, "回包字段不匹配：" + Convert.ToHexString(response));
            return null;
        }

        return response[offset + 8];
    }

    private static byte Crc(byte[] buffer, int offset)
    {
        byte crc = 0;
        for (int i = offset + 2; i < offset + 88; i++) crc ^= buffer[i];
        return crc;
    }

    // ================= 2023 款 PA / PI 帧（实验性） =================

    private static BatteryReading? QueryPaPi(HidInterface iface, RazerDevice device)
    {
        try
        {
            using SafeFileHandle handle = HidNative.OpenDevice(iface.Path, true);
            if (handle.IsInvalid) return null;

            var request = new byte[64];
            request[0] = 0x02;   // report id
            request[1] = 0x80;
            request[2] = 0x0A;   // inner 长度
            request[3] = 0x00;
            request[4] = 0x00;
            request[5] = 0x50;   // 'P'
            request[6] = 0x41;   // 'A'
            request[7] = 0x00;
            request[8] = 0x08;   // 目标：耳机本体
            request[9] = 0x21;   // 命令：取电量

            if (!HidNative.WriteFile(handle, request, request.Length, out _, IntPtr.Zero)) return null;

            var report = new byte[64];
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (HidNative.WaitForSingleObject(handle.DangerousGetHandle(), 400) != 0) return null;
                if (!HidNative.ReadFile(handle, report, report.Length, out int read, IntPtr.Zero)) return null;
                if (read < 8) continue;

                int? level = ScanPaPi(report, 0x21);
                if (level is >= 0 and <= 100)
                {
                    int? charging = null;
                    if (TryAskCharging(handle, report)) charging = ScanPaPi(report, 0x2A);
                    return new BatteryReading(level.Value, false, charging is > 0);
                }
            }

            LogOnce(device, "PA/PI 帧未匹配到电量，最近一帧：" + Convert.ToHexString(report));
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("razer papi failed: " + ex.Message);
            return null;
        }
    }

    private static bool TryAskCharging(SafeFileHandle handle, byte[] report)
    {
        var request = new byte[64];
        request[0] = 0x02;
        request[1] = 0x80;
        request[2] = 0x0A;
        request[5] = 0x50;
        request[6] = 0x41;
        request[7] = 0x00;
        request[8] = 0x08;
        request[9] = 0x2A;

        if (!HidNative.WriteFile(handle, request, request.Length, out _, IntPtr.Zero)) return false;
        if (HidNative.WaitForSingleObject(handle.DangerousGetHandle(), 400) != 0) return false;
        return HidNative.ReadFile(handle, report, report.Length, out _, IntPtr.Zero);
    }

    /// <summary>在一帧里找 "PI" 子帧，返回命令后面的第一个字节。</summary>
    private static int? ScanPaPi(byte[] buffer, byte command)
    {
        for (int i = 0; i + 7 < buffer.Length; i++)
        {
            if (buffer[i] != 0x50 || buffer[i + 1] != 0x49) continue; // "PI"

            // 子帧头 4 字节 + 命令字节
            if (buffer[i + 4] == command) return buffer[i + 5];
            if (buffer[i + 5] == command) return buffer[i + 6];
        }

        return null;
    }

    private static void LogOnce(RazerDevice device, string message)
    {
        if (!LoggedFailures.Add(device.Name + "#log")) return;
        Log.Write($"razer[{device.Name}]: {message}");
    }

    public static string Describe() => string.Join(", ", Devices.Select(d => d.Name));
}
