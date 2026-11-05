using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace PowerIsland.Services;

/// <summary>
/// 动态生成托盘图标：可直接画出电量数字，也可只画电量条。
/// 多尺寸 ICO（16/24/32/48），保证 100% 和 150% 缩放下都清晰。
/// </summary>
public static class TrayIconFactory
{
    private static readonly int[] Sizes = { 16, 24, 32, 48 };

    public static Icon Create(int? level, bool showNumber, int lowThreshold)
    {
        bool known = level.HasValue;
        int value = level ?? 0;
        Color accent = !known ? Color.FromArgb(255, 168, 174, 182)
            : value <= lowThreshold ? Color.FromArgb(255, 255, 90, 82)
            : Color.FromArgb(255, 45, 211, 107);

        var frames = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using Bitmap bmp = showNumber
                ? DrawNumber(size, known ? value : null, accent)
                : DrawGlyph(size, known ? value : null, accent);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            frames.Add(ms.ToArray());
        }

        byte[] ico = BuildIco(frames);
        using var stream = new MemoryStream(ico);
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    private static Bitmap NewCanvas(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        return bmp;
    }

    private static Bitmap DrawNumber(int size, int? level, Color accent)
    {
        Bitmap bmp = NewCanvas(size);
        using (var g = Graphics.FromImage(bmp))
        {
            string text = level.HasValue ? level.Value.ToString() : "--";

            // 位数越多字越小，保证两位数能塞满整个图标、三位数不溢出
            float fontSize = text.Length switch
            {
                >= 3 => size * 0.52f,
                2 => size * 0.72f,
                _ => size * 0.80f
            };

            using var family = new FontFamily("Segoe UI");
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Bold, fontSize, new PointF(0, 0),
                StringFormat.GenericTypographic);

            // 按字形实际边界居中，数字才能顶到图标边缘
            RectangleF bounds = path.GetBounds();
            using (var shift = new Matrix())
            {
                shift.Translate((size - bounds.Width) / 2f - bounds.X,
                    (size - bounds.Height) / 2f - bounds.Y);
                path.Transform(shift);
            }

            // 一圈细描边：浅色任务栏上也能看清，深色任务栏上不碍事
            if (size >= 16)
            {
                using var outline = new Pen(Color.FromArgb(150, 16, 18, 22), Math.Max(0.8f, size * 0.05f))
                {
                    LineJoin = LineJoin.Round
                };
                g.DrawPath(outline, path);
            }

            using var fill = new SolidBrush(accent);
            g.FillPath(fill, path);
        }
        return bmp;
    }

    private static Bitmap DrawGlyph(int size, int? level, Color accent)
    {
        Bitmap bmp = NewCanvas(size);
        using (var g = Graphics.FromImage(bmp))
        {
            float w = size * 0.80f;
            float h = size * 0.46f;
            float x = size * 0.06f;
            float y = (size - h) / 2f;
            float nubW = size * 0.07f;

            using var body = RoundedRect(new RectangleF(x, y, w, h), h * 0.28f);
            using var outline = new Pen(Color.FromArgb(235, 248, 250, 252), Math.Max(1.1f, size * 0.085f));
            g.DrawPath(outline, body);

            using var nub = new SolidBrush(Color.FromArgb(235, 248, 250, 252));
            g.FillRectangle(nub, x + w + nubW * 0.25f, y + h * 0.3f, nubW * 0.55f, h * 0.4f);

            if (level.HasValue)
            {
                float ratio = Math.Clamp(level.Value / 100f, 0.04f, 1f);
                float innerW = (w - size * 0.1f) * ratio;
                using var brush = new SolidBrush(accent);
                using var inner = RoundedRect(new RectangleF(x + size * 0.05f, y + size * 0.05f, innerW, h - size * 0.1f),
                    (h - size * 0.1f) * 0.3f);
                g.FillPath(brush, inner);
            }
        }
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Lighten(Color c) => Color.FromArgb(c.A,
        Math.Min(255, c.R + 40), Math.Min(255, c.G + 40), Math.Min(255, c.B + 40));

    private static byte[] BuildIco(List<byte[]> frames)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            int size = Sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }

        foreach (byte[] frame in frames) writer.Write(frame);
        writer.Flush();
        return ms.ToArray();
    }
}
