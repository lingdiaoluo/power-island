using System.Windows;

namespace PowerIsland.Views;

public partial class TrayPopup : Window
{
    public TrayPopup()
    {
        InitializeComponent();
    }

    public void ShowNear(System.Drawing.Point cursor)
    {
        try
        {
            if (!IsVisible) Show();
            UpdateLayout();

            PresentationSource? source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(cursor);
            System.Drawing.Rectangle area = screen.WorkingArea;

            double width = ActualWidth > 0 ? ActualWidth : 190;
            double height = ActualHeight > 0 ? ActualHeight : 70;

            double left = cursor.X / scale - width + 18;
            double top = cursor.Y / scale - height - 46;

            double maxLeft = area.Right / scale - width - 8;
            double minLeft = area.Left / scale + 8;
            left = Math.Min(Math.Max(left, minLeft), maxLeft);
            top = Math.Max(top, area.Top / scale + 8);

            Left = left;
            Top = top;
        }
        catch (Exception ex)
        {
            Services.Log.Write("POPUP: " + ex);
        }
    }

    /// <summary>光标是否落在气泡窗口内（含 4px 容差）。</summary>
    public bool ContainsCursor()
    {
        if (!IsVisible) return false;
        PresentationSource? source = PresentationSource.FromVisual(this);
        double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
        double x = p.X / scale;
        double y = p.Y / scale;
        return x >= Left - 4 && x <= Left + ActualWidth + 4 &&
               y >= Top - 4 && y <= Top + ActualHeight + 4;
    }

    public void HidePopup()
    {
        if (IsVisible) Hide();
    }
}
