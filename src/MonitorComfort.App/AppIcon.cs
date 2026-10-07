using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MonitorComfort.App;

/// <summary>The app mark, a sun setting on a horizon, drawn at runtime so window and tray share one glyph.</summary>
internal static class AppIcon
{
    private static readonly Color Tile = Color.FromRgb(0x24, 0x3B, 0x5A);
    private static readonly Color Sun = Color.FromRgb(0xF2, 0xA9, 0x3B);
    private static readonly Color PausedSun = Color.FromRgb(0x9A, 0xA3, 0xAD);

    public static RenderTargetBitmap Render(int size, bool paused = false)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        var s = size / 32.0;
        var horizon = 20 * s;

        using (var ctx = bitmap.CreateDrawingContext())
        {
            var tileBrush = new SolidColorBrush(Tile);
            ctx.DrawRectangle(tileBrush, null, new RoundedRect(new Rect(1 * s, 1 * s, size - (2 * s), size - (2 * s)), 7 * s));

            using (ctx.PushClip(new Rect(0, 0, size, horizon)))
            {
                ctx.DrawEllipse(new SolidColorBrush(paused ? PausedSun : Sun), null, new Point(16 * s, horizon), 9 * s, 9 * s);
            }

            var line = new Pen(Brushes.White, 2 * s, lineCap: PenLineCap.Round);
            ctx.DrawLine(line, new Point(6 * s, horizon + (1.5 * s)), new Point(26 * s, horizon + (1.5 * s)));
            ctx.DrawLine(line, new Point(10 * s, horizon + (5.5 * s)), new Point(22 * s, horizon + (5.5 * s)));
        }

        return bitmap;
    }
}
