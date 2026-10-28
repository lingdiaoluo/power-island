using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PowerIsland.Interop;

namespace PowerIsland.Services;

public sealed record HidInterface(
    string Path,
    ushort Vid,
    ushort Pid,
    string Product,
    string Manufacturer,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    int FeatureLength);

/// <summary>枚举本机所有 HID 顶层集合，供电量读取使用。</summary>
public static class HidInventory
{
    public static List<HidInterface> Enumerate()
    {
        var list = new List<HidInterface>();
        var guid = HidNative.HidGuid;
        IntPtr set = HidNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;

        var iface = new HidNative.SP_DEVICE_INTERFACE_DATA
        {
            cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>()
        };

        try
        {
            for (int i = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
            {
                HidNative.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                if (needed <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, needed, out _, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (path.Length == 0) continue;

                    var parsed = TryReadInterface(path);
                    if (parsed != null) list.Add(parsed);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }

        return list;
    }

    private static HidInterface? TryReadInterface(string path)
    {
        SafeFileHandle handle = HidNative.OpenDevice(path, false);
        if (handle.IsInvalid) return null;

        try
        {
            var attr = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
            if (!HidNative.HidD_GetAttributes(handle, ref attr)) return null;

            var caps = new HidNative.HIDP_CAPS();
            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
            try
            {
                HidNative.HidP_GetCaps(preparsed, ref caps);
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }

            var productBuf = new char[256];
            string product = HidNative.HidD_GetProductString(handle, productBuf, 512)
                ? new string(productBuf).TrimEnd('\0')
                : "";
            var manBuf = new char[256];
            string manufacturer = HidNative.HidD_GetManufacturerString(handle, manBuf, 512)
                ? new string(manBuf).TrimEnd('\0')
                : "";

            return new HidInterface(path, attr.VendorID, attr.ProductID, product, manufacturer,
                caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                caps.FeatureReportByteLength);
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
}
