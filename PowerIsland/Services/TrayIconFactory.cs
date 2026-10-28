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
        Color accent = !known ? Color.FromArgb(255, 138, 144, 153)
            : value <= lowThreshold / 2 ? Color.FromArgb(255, 220, 38, 38)
            : value <= lowThreshold ? Color.FromArgb(255, 239, 108, 0)
            : Color.FromArgb(255, 22, 163, 74);

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
            float pad = Math.Max(0.5f, size * 0.03f);
            var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
            using var path = RoundedRect(rect, size * 0.24f);
            using var fill = new LinearGradientBrush(rect, Lighten(accent), accent, 90f);
            g.FillPath(fill, path);

            string text = level.HasValue ? level.Value.ToString() : "--";
            float fontSize = level is >= 100 ? size * 0.40f : level.HasValue ? size * 0.48f : size * 0.42f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, -size * 0.02f, size, size), format);
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
