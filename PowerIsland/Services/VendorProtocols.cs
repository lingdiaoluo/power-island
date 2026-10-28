using Microsoft.Win32.SafeHandles;
using PowerIsland.Interop;
using PowerIsland.Models;

namespace PowerIsland.Services;

public sealed record KnownDevice(
    string Name,
    string Meta,
    ushort Vid,
    ushort PidWireless,
    ushort PidWired,
    ushort UsagePage,
    ushort Usage,
    int Protocol,
    DeviceKind Kind = DeviceKind.Mouse);

public readonly record struct BatteryReading(int Level, bool Wired);

/// <summary>
/// Compx 2.4G 接收器（VGN / VXE / ATK / Pulsar 同源）的私有电量协议。
/// 协议 1：17 字节报告，写入 0x08 + 0x04 … 0x49，应答第 6 字节为电量、第 7 字节为有线标记。
/// 协议 2：64 字节报告，命令 0x72，无线应答第 7 字节为电量。
/// </summary>
public static class CompxReader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Reader> Readers = new();

    /// <summary>
    /// 每台设备一个常驻读线程：写入查询后就在后台阻塞等应答。
    /// 这样即使鼠标休眠、接收器不回包，也只会卡住这个后台线程，界面照常刷新。
    /// </summary>
    public static BatteryReading? Query(HidInterface iface, int protocol, int waitMs = 1500)
    {
        Reader reader = GetReader(iface, protocol);
        reader.Request(waitMs);
        return reader.Reading;
    }

    private static Reader GetReader(HidInterface iface, int protocol)
    {
        lock (Gate)
        {
            string key = iface.Path + "#" + protocol;
            if (Readers.TryGetValue(key, out Reader? existing)) return existing;
            var reader = new Reader(iface.Path, protocol);
            Readers[key] = reader;
            return reader;
        }
    }

    private sealed class Reader
    {
        private readonly string _path;
        private readonly int _protocol;
        private readonly AutoResetEvent _request = new(false);
        private readonly ManualResetEventSlim _fresh = new(false);
        private readonly object _gate = new();
        private SafeFileHandle? _handle;
        private volatile bool _busy;
        private BatteryReading? _reading;
        private Thread? _thread;

        public Reader(string path, int protocol)
        {
            _path = path;
            _protocol = protocol;
        }

        public BatteryReading? Reading => _reading;

        public void Request(int waitMs)
        {
            EnsureThread();

            // 上一次读还在等应答（接收器没回包）时，先关掉句柄取消这个挂起的读，
            // 否则新命令会一直排在它后面，设备看起来就像“死”了。
            lock (_gate)
            {
                if (_busy)
                {
                    try { _handle?.Dispose(); } catch { /* 忽略 */ }
                }
            }

            _fresh.Reset();
            _request.Set();

            if (!_fresh.Wait(waitMs))
            {
                // 等不到应答就立刻取消这次挂起的读，把接收器让出来给下一次查询
                lock (_gate)
                {
                    try { _handle?.Dispose(); } catch { /* 忽略 */ }
                }
            }
        }

        private void EnsureThread()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "hid-battery-reader"
            };
            _thread.Start();
        }

        private void Loop()
        {
            while (true)
            {
                _request.WaitOne();
                try
                {
                    ReadOnce();
                }
                catch (Exception ex)
                {
                    Log.Write("battery reader: " + ex.Message);
                }
            }
        }

        private void ReadOnce()
        {
            SafeFileHandle handle = HidNative.OpenDevice(_path, true);
            if (handle.IsInvalid) return;

            _busy = true;
            lock (_gate) _handle = handle;

            try
            {
                byte[] request = _protocol == 2 ? BuildProtocol2() : BuildProtocol1();
                if (!HidNative.WriteFile(handle, request, request.Length, out _, IntPtr.Zero)) return;

                var response = new byte[request.Length];
                if (!HidNative.ReadFile(handle, response, response.Length, out int read, IntPtr.Zero)) return;
                if (read < 8) return;

                BatteryReading? parsed = Parse(_protocol, response);
                if (parsed == null) return;

                _reading = parsed.Value;
                _fresh.Set();
            }
            finally
            {
                lock (_gate) _handle = null;
                _busy = false;
                handle.Dispose();
            }
        }

        private static byte[] BuildProtocol1()
        {
            var request = new byte[17];
            request[0] = 0x08;
            request[1] = 0x04;
            request[16] = 0x49;
            return request;
        }

        private static byte[] BuildProtocol2()
        {
            var request = new byte[64];
            request[0] = 0x08;
            request[1] = 0x7D;   // 无线
            request[2] = 0x72;
            request[3] = 0x02;
            request[4] = 0x00;
            request[5] = 0x01;
            request[6] = 0x07;
            request[7] = 0x01;
            return request;
        }

        private static BatteryReading? Parse(int protocol, byte[] response)
        {
            if (protocol == 2)
            {
                if (response[1] != 0x72 || response[5] != 0x07) return null;
                int level2 = response[7];
                return level2 is >= 0 and <= 100 ? new BatteryReading(level2, false) : null;
            }

            if (response[0] != 0x08 || response[1] != 0x04) return null;
            int level = response[6];
            return level is >= 0 and <= 100 ? new BatteryReading(level, response[7] != 0) : null;
        }
    }
}

public static class KnownDevices
{
    public static readonly IReadOnlyList<KnownDevice> All = new List<KnownDevice>
    {
        // VGN / VXE（VID 3554）
        new("VGN F1 MOBA", "PAW3395 · 2.4G · Compx 接收器", 0x3554, 0xF503, 0xF502, 0xFF02, 0x0002, 1),
        new("VGN F1 Pro", "PAW3395 · 2.4G · Compx 接收器", 0x3554, 0xF503, 0xF502, 0xFF02, 0x0002, 1),
        new("VXE R1 Pro Max", "PAW3395 · 2.4G · Compx 接收器", 0x3554, 0xF58A, 0xF58C, 0xFF02, 0x0002, 1),
        new("VXE R1 SE+", "PAW3395 · 2.4G · Compx 接收器", 0x3554, 0xF58E, 0xF58F, 0xFF02, 0x0002, 1),

        // ATK / VXE（VID 373B）
        new("ATK F1 Ultimate", "PAW3950 · 2.4G · Nordic 52840", 0x373B, 0x1031, 0x102E, 0xFF02, 0x0002, 1),
        new("ATK A9 Ultimate", "PAW3950 · 2.4G · Nordic 52840", 0x373B, 0x11D9, 0x11B6, 0xFF02, 0x0002, 1),
        new("VXE MAD R", "PAW3950 · 2.4G · Nordic 52840", 0x373B, 0x104D, 0x103F, 0xFF02, 0x0002, 1),
        new("VXE MAD R Major+", "PAW3950 · 2.4G · Nordic 52840", 0x373B, 0x1040, 0x104C, 0xFF02, 0x0002, 1),
        new("ATK Zero", "PAW3950 · 2.4G · Nordic 54L15", 0x373B, 0x1155, 0x1154, 0xFF05, 0x0001, 2)
    };

    /// <summary>同一 PID 只会命中第一条，避免 F1 MOBA / F1 Pro 重复显示。</summary>
    public static IEnumerable<KnownDevice> Distinct()
    {
        var seen = new HashSet<(ushort, ushort)>();
        foreach (var device in All)
        {
            if (seen.Add((device.Vid, device.PidWireless))) yield return device;
        }
    }
}
