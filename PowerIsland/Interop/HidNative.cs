using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PowerIsland.Interop;

internal static class HidNative
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;

    public static readonly Guid HidGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsed, ref HIDP_CAPS caps);

    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public const int HidP_Input = 0;
    public const int HidP_Output = 1;
    public const int HidP_Feature = 2;

    /// <summary>从报告缓冲区里取出某个 usage 的数值（标准 HID 电量走这里）。</summary>
    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint value, IntPtr preparsed, byte[] report, int reportLength);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetProductString(SafeFileHandle h, char[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle h, char[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetSerialNumberString(SafeFileHandle h, char[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buffer, int len);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] buffer, int len);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData, ref Guid interfaceClassGuid,
        int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA iface,
        IntPtr ifaceDetail, int ifaceDetailSize, out int requiredSize, IntPtr devInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        byte[]? inBuffer, int inSize, byte[]? outBuffer, int outSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int count, out int read, ref OVERLAPPED_IO overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, ref OVERLAPPED_IO overlapped);

    public const uint IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0197;
    public const uint IOCTL_HID_GET_INDEXED_STRING = 0x000B01E0;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public static SafeFileHandle OpenDevice(string path, bool readWrite = true)
    {
        var access = readWrite ? GENERIC_READ | GENERIC_WRITE : 0u;
        return CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    /// <summary>只读句柄：能读报告但不能写，适合只查询电量的设备。</summary>
    public static SafeFileHandle OpenDeviceRead(string path)
    {
        return CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    public static SafeFileHandle OpenDeviceOverlapped(string path, bool readWrite = true)
    {
        var access = readWrite ? GENERIC_READ | GENERIC_WRITE : 0u;
        return CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED_IO
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initialState, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(SafeFileHandle h, ref OVERLAPPED_IO overlapped,
        out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(SafeFileHandle h, ref OVERLAPPED_IO overlapped);

    /// <summary>Overlapped read with timeout, so a sleeping mouse never blocks the UI thread.</summary>
    public static bool TryRead(SafeFileHandle h, byte[] buffer, int timeoutMs, out int bytesRead)
    {
        bytesRead = 0;
        var evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        var ov = new OVERLAPPED_IO { hEvent = evt };
        try
        {
            if (ReadFile(h, buffer, buffer.Length, out _, ref ov) == false)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 997) return false; // ERROR_IO_PENDING
            }
            uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
            if (wait != 0) // timeout / abandoned
            {
                CancelIoEx(h, ref ov);
                return false;
            }
            return GetOverlappedResult(h, ref ov, out bytesRead, false) && bytesRead > 0;
        }
        finally
        {
            CloseHandle(evt);
        }
    }

    public static bool TryWrite(SafeFileHandle h, byte[] buffer, int timeoutMs)
    {
        var evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        var ov = new OVERLAPPED_IO { hEvent = evt };
        try
        {
            if (WriteFile(h, buffer, buffer.Length, out _, ref ov) == false)
            {
                if (Marshal.GetLastWin32Error() != 997) return false;
            }
            return WaitForSingleObject(evt, (uint)timeoutMs) == 0 && GetOverlappedResult(h, ref ov, out _, false);
        }
        finally
        {
            CloseHandle(evt);
        }
    }
}
