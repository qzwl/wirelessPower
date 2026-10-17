using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryTray.UI;

internal static class BatteryIconRenderer
{
    private static readonly Color Green = Color.FromArgb(104, 219, 142);
    private static readonly Color Yellow = Color.FromArgb(245, 196, 81);
    private static readonly Color Red = Color.FromArgb(255, 96, 96);
    private static readonly Color Neutral = Color.FromArgb(155, 160, 166);
    private static readonly Color Outline = Color.FromArgb(250, 250, 250);
    private static readonly Color Shadow = Color.FromArgb(185, 8, 10, 14);
    private static readonly Color Badge = Color.FromArgb(23, 59, 143);
    private static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48];

    public static Color LevelColor(int? level)
    {
        if (level is null)
        {
            return Neutral;
        }

        return level >= 60 ? Green : level >= 20 ? Yellow : Red;
    }

    public static string LevelColorName(int? level)
    {
        if (level is null)
        {
            return "未知";
        }

        return level >= 60 ? "浅绿" : level >= 20 ? "黄色" : "红色";
    }

    public static Icon CreateTrayIcon(int? level, bool charging, bool bluetooth = false)
    {
        byte[] data = BuildIco(level, charging, TraySizes, bluetooth);
        using var stream = new MemoryStream(data);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static Bitmap CreateBitmap(
        int? level, int size, bool charging = false, bool bluetooth = false)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        Draw(graphics, size, level, charging, bluetooth);
        return bitmap;
    }

    public static byte[] BuildIco(
        int? level, bool charging, IReadOnlyList<int> sizes, bool bluetooth = false)
    {
        var frames = sizes.Select(size =>
        {
            using Bitmap bitmap = CreateBitmap(level, size, charging, bluetooth);
            return (size, data: ToDib(bitmap));
        }).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in frames)
        {
            writer.Write(data);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static Bitmap CreatePreview()
    {
        var bitmap = new Bitmap(700, 560, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(30, 32, 36));
        using var light = new SolidBrush(Color.FromArgb(242, 242, 244));
        graphics.FillRectangle(light, 0, 280, 700, 280);

        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 10, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);
        using var black = new SolidBrush(Color.FromArgb(30, 30, 30));

        graphics.DrawString("深色任务栏", titleFont, white, 12, 10);
        graphics.DrawString("浅色任务栏", titleFont, black, 12, 290);

        var cases = new (int? Level, bool Charging, bool Bluetooth, string Label)[]
        {
            (88, false, false, "88% 浅绿"),
            (45, false, false, "45% 黄色"),
            (12, false, false, "12% 红色"),
            (null, false, false, "未发现"),
            (67, false, true, "67% 蓝牙")
        };

        int[] sizes = [16, 24, 32, 48, 64];
        for (int row = 0; row < 2; row++)
        {
            int rowOffset = row * 280;
            int x = 12;
            foreach (var item in cases)
            {
                int top = 30 + rowOffset;
                foreach (int size in sizes)
                {
                    using Bitmap icon = CreateBitmap(
                        item.Level, size, item.Charging, item.Bluetooth);
                    graphics.DrawImageUnscaled(icon, x, top);
                    top += size + 5;
                }

                graphics.DrawString(item.Label, labelFont,
                    row == 0 ? white : black, x, 250 + rowOffset);
                x += 132;
            }
        }

        return bitmap;
    }

    private static void Draw(
        Graphics graphics, int size, int? level, bool charging, bool bluetooth)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        float s = size / 32f;
        float left = 3.2f * s;
        float top = 6.7f * s;
        float width = 21.2f * s;
        float height = 15.4f * s;
        float radius = 3.1f * s;
        var body = new RectangleF(left, top, width, height);

        using var bodyPath = RoundedRect(body, radius);
        using var shadowPen = new Pen(Shadow, Math.Max(2.2f * s, 1.4f));
        using var outlinePen = new Pen(Outline, Math.Max(1.55f * s, 1.0f));
        graphics.DrawPath(shadowPen, bodyPath);
        graphics.DrawPath(outlinePen, bodyPath);

        var capSize = new SizeF(3.0f * s, height * 0.42f);
        var capRect = new RectangleF(left + width - 0.8f * s,
            top + height * 0.29f, capSize.Width, capSize.Height);
        using var capPath = RoundedRect(capRect, 1.1f * s);
        graphics.DrawPath(shadowPen, capPath);
        graphics.DrawPath(outlinePen, capPath);

        var inner = RectangleF.Inflate(body, -2.6f * s, -2.6f * s);
        using (var innerBrush = new SolidBrush(Color.FromArgb(58, 8, 10, 14)))
        {
            graphics.FillRectangle(innerBrush, inner);
        }

        if (level is not null)
        {
            float fraction = Math.Clamp(level.Value / 100f, 0f, 1f);
            float fillWidth = inner.Width * fraction;
            if (level.Value <= 20)
            {
                fillWidth = Math.Max(fillWidth, 1.6f * s);
            }

            if (fillWidth > 0)
            {
                var fillRect = new RectangleF(inner.X, inner.Y, fillWidth, inner.Height);
                using var fillBrush = new LinearGradientBrush(
                    fillRect,
                    Lighten(LevelColor(level), 0.18f),
                    LevelColor(level),
                    LinearGradientMode.Vertical);
                graphics.FillRectangle(fillBrush, fillRect);
            }
        }

        if (charging)
        {
            DrawBolt(graphics, new RectangleF(
                inner.X + inner.Width * 0.34f,
                inner.Y + inner.Height * 0.10f,
                inner.Width * 0.30f,
                inner.Height * 0.80f), s);
        }

        float badgeSize = Math.Max(7.2f * s, size * 0.41f);
        float badgeX = size - badgeSize - 0.8f * s;
        float badgeY = size - badgeSize - 0.8f * s;
        var badgeRect = new RectangleF(badgeX, badgeY, badgeSize, badgeSize);

        using (var badgeShadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            graphics.FillEllipse(badgeShadow,
                badgeRect.X - s, badgeRect.Y - s,
                badgeRect.Width + 2 * s, badgeRect.Height + 2 * s);
        }

        using (var badgeBrush = new SolidBrush(Badge))
        {
            graphics.FillEllipse(badgeBrush, badgeRect);
        }

        using (var badgePen = new Pen(Color.FromArgb(245, 245, 245),
                   Math.Max(1.15f * s, 0.9f)))
        {
            graphics.DrawEllipse(badgePen, badgeRect);
        }

        if (bluetooth)
        {
            DrawBluetoothGlyph(graphics, badgeRect, s);
        }
        else
        {
            DrawUsbGlyph(graphics, badgeRect, s);
        }
    }

    private static void DrawUsbGlyph(Graphics graphics, RectangleF badge, float s)
    {
        float cx = badge.X + badge.Width / 2f;
        float cy = badge.Y + badge.Height / 2f;
        float unit = badge.Width / 14f;
        using var pen = new Pen(Color.White, Math.Max(1.25f * s, 1.0f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var white = new SolidBrush(Color.White);

        graphics.DrawLine(pen, cx, cy - 4.2f * unit, cx, cy + 4.0f * unit);
        graphics.DrawLine(pen, cx, cy + 1.2f * unit, cx - 3.3f * unit, cy - 0.9f * unit);
        graphics.DrawLine(pen, cx, cy - 0.7f * unit, cx + 3.3f * unit, cy + 1.4f * unit);

        float dot = Math.Max(1.55f * unit, 1.0f);
        graphics.FillEllipse(white, cx - 4.1f * unit, cy - 2.0f * unit, dot, dot);
        graphics.FillRectangle(white, cx + 3.1f * unit, cy + 0.8f * unit, dot, dot);

        PointF[] arrow =
        [
            new(cx, cy - 6.2f * unit),
            new(cx - 1.8f * unit, cy - 3.7f * unit),
            new(cx + 1.8f * unit, cy - 3.7f * unit)
        ];
        graphics.FillPolygon(white, arrow);
    }

    private static void DrawBluetoothGlyph(Graphics graphics, RectangleF badge, float s)
    {
        float cx = badge.X + badge.Width / 2f;
        float cy = badge.Y + badge.Height / 2f;
        float u = badge.Width / 14f;
        using var pen = new Pen(Color.White, Math.Max(1.3f * s, 1.0f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        PointF[] points =
        [
            new(cx, cy - 4.6f * u),
            new(cx + 3.4f * u, cy - 1.8f * u),
            new(cx - 3.4f * u, cy + 1.8f * u),
            new(cx + 3.4f * u, cy + 4.6f * u),
            new(cx, cy + 4.6f * u)
        ];
        graphics.DrawLines(pen, points);
        graphics.DrawLine(pen, cx, cy - 4.6f * u, cx, cy + 4.6f * u);
    }

    private static void DrawBolt(Graphics graphics, RectangleF rect, float s)
    {
        PointF[] points =
        [
            new(rect.X + rect.Width * 0.56f, rect.Y),
            new(rect.X + rect.Width * 0.18f, rect.Y + rect.Height * 0.56f),
            new(rect.X + rect.Width * 0.47f, rect.Y + rect.Height * 0.56f),
            new(rect.X + rect.Width * 0.36f, rect.Y + rect.Height),
            new(rect.X + rect.Width * 0.84f, rect.Y + rect.Height * 0.38f),
            new(rect.X + rect.Width * 0.54f, rect.Y + rect.Height * 0.38f)
        ];

        using var brush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        using var pen = new Pen(Color.FromArgb(185, 0, 0, 0), Math.Max(0.8f * s, 0.6f));
        graphics.FillPolygon(brush, points);
        graphics.DrawPolygon(pen, points);
    }

    private static GraphicsPath RoundedRect(RectangleF rectangle, float radius)
    {
        float diameter = Math.Max(radius * 2, 0.1f);
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter,
            diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Lighten(Color color, float amount)
    {
        return Color.FromArgb(
            color.A,
            Math.Min(255, (int)(color.R + (255 - color.R) * amount)),
            Math.Min(255, (int)(color.G + (255 - color.G) * amount)),
            Math.Min(255, (int)(color.B + (255 - color.B) * amount)));
    }

    private static byte[] ToDib(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int maskStride = ((width + 31) / 32) * 4;
            int xorSize = width * height * 4;
            int andSize = maskStride * height;
            using var stream = new MemoryStream(40 + xorSize + andSize);
            using var writer = new BinaryWriter(stream);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(xorSize + andSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * 4;
                    writer.Write(pixels[index + 2]); // B
                    writer.Write(pixels[index + 1]); // G
                    writer.Write(pixels[index]);     // R
                    writer.Write(pixels[index + 3]); // A
                }
            }

            writer.Write(new byte[andSize]);
            writer.Flush();
            return stream.ToArray();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
