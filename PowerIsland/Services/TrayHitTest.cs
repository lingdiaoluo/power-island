using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace PowerIsland.Services;

/// <summary>
/// Windows 11 的任务栏不会把悬停消息转给托盘图标窗口，
/// 这里用 Shell_NotifyIconGetRect 拿到图标矩形，再自己判断光标是否停在图标上。
/// </summary>
public static class TrayHitTest
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point point);

    public static System.Drawing.Point CursorPosition() => GetCursorPos(out System.Drawing.Point p) ? p : default;

    /// <summary>返回托盘图标的屏幕矩形；失败时返回 null。</summary>
    public static System.Drawing.Rectangle? GetIconRect(WinForms.NotifyIcon icon)
    {
        try
        {
            var windowField = typeof(WinForms.NotifyIcon).GetField("_window",
                                BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? typeof(WinForms.NotifyIcon).GetField("window",
                                BindingFlags.NonPublic | BindingFlags.Instance);
            object? nativeWindow = windowField?.GetValue(icon);
            if (nativeWindow == null)
            {
                Log.Write("hit: field 'window' null; fields=" + string.Join(",",
                    typeof(WinForms.NotifyIcon).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => f.Name + ":" + f.FieldType.Name)));
                return null;
            }

            var handleProp = nativeWindow.GetType().GetProperty("Handle",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handleProp?.GetValue(nativeWindow) is not IntPtr handle || handle == IntPtr.Zero) return null;

            var idField = typeof(WinForms.NotifyIcon).GetField("_id",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? typeof(WinForms.NotifyIcon).GetField("id",
                            BindingFlags.NonPublic | BindingFlags.Instance);
            if (idField?.GetValue(icon) is not uint id)
            {
                Log.Write("hit: id field missing; value=" + idField?.GetValue(icon));
                return null;
            }

            var identifier = new NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = handle,
                uID = id,
                guidItem = Guid.Empty
            };

            int hr = Shell_NotifyIconGetRect(ref identifier, out RECT rect);
            if (hr != 0)
            {
                Log.Write($"hit: GetRect hr=0x{hr:X8} hwnd={handle} id={id}");
                return null;
            }

            return new System.Drawing.Rectangle(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        catch (Exception ex)
        {
            Log.Write("tray rect failed: " + ex.Message);
            return null;
        }
    }
}
