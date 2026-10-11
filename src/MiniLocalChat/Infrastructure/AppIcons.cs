using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace MiniLocalChat.Infrastructure;

/// <summary>Draws the chat-bubble app icon at runtime, so there are no binary assets to ship.</summary>
internal static class AppIcons
{
    private static ImageSource? _windowIcon;

    public static ImageSource WindowIcon => _windowIcon ??= CreateWindowIcon();

    public static Icon CreateIcon(int size, bool unreadDot)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var s = size / 32f;

            using var bubble = new GraphicsPath { FillMode = FillMode.Winding };
            AddRoundedRect(bubble, new RectangleF(1 * s, 4 * s, 28 * s, 19 * s), 6 * s);
            bubble.AddPolygon([new PointF(7 * s, 20 * s), new PointF(7 * s, 29 * s), new PointF(15 * s, 22 * s)]);
            using (var fill = new SolidBrush(Color.FromArgb(0x5B, 0x5F, 0xC7)))
            {
                g.FillPath(fill, bubble);
            }

            using (var dot = new SolidBrush(Color.White))
            {
                foreach (var x in new[] { 8.5f, 15f, 21.5f })
                {
                    g.FillEllipse(dot, (x - 2) * s, 11.5f * s, 4 * s, 4 * s);
                }
            }

            if (unreadDot)
            {
                using var green = new SolidBrush(Color.FromArgb(0x22, 0xC5, 0x5E));
                using var ring = new Pen(Color.White, 2 * s);
                g.FillEllipse(green, 19 * s, 0.5f * s, 12 * s, 12 * s);
                g.DrawEllipse(ring, 19 * s, 0.5f * s, 12 * s, 12 * s);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Win32.DestroyIcon(handle);
        }
    }

    private static ImageSource CreateWindowIcon()
    {
        using var icon = CreateIcon(64, unreadDot: false);
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static void AddRoundedRect(GraphicsPath path, RectangleF r, float radius)
    {
        var d = radius * 2;
        path.StartFigure();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
