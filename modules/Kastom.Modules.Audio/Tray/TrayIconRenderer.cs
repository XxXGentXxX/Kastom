using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kastom.Modules.Audio.Tray;

/// <summary>Який гліф звуку намальовано на значку в області сповіщень.</summary>
public enum TrayGlyph
{
    /// <summary>Немає пристрою виводу чи звук вимкнено.</summary>
    Muted,

    /// <summary>Гучність менше 1 %.</summary>
    Volume0,

    /// <summary>Менше 33 %.</summary>
    Volume1,

    /// <summary>Менше 66 %.</summary>
    Volume2,

    /// <summary>Від 66 %.</summary>
    Volume3,
}

/// <summary>Малює значок звуку гліфом Segoe Fluent Icons під колір панелі задач; за потреби — з позначкою вимкненого мікрофона.</summary>
internal static class TrayIconRenderer
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static System.Drawing.Icon Render(TrayGlyph glyph, bool micBadge, bool lightTaskbar, int sizePx)
    {
        var brush = new SolidColorBrush(lightTaskbar ? Colors.Black : Colors.White);
        brush.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (micBadge)
            {
                // Навколо позначки гліф вирізано з відступом: на 16 px червоне коло не зливається з контурами динаміка.
                var (centre, radius) = BadgeCircle(sizePx);
                var halo = new EllipseGeometry(centre, radius + sizePx / 16.0, radius + sizePx / 16.0);
                var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, sizePx, sizePx)), halo);
                clip.Freeze();
                context.PushClip(clip);
                DrawGlyph(context, CharOf(glyph), sizePx, brush);
                context.Pop();
                DrawBadge(context, sizePx, lightTaskbar);
            }
            else
            {
                DrawGlyph(context, CharOf(glyph), sizePx, brush);
            }
        }

        var target = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return ToIcon(target, sizePx);
    }

    // Коло позначки: діаметр — половина значка (не менше 8 px), притиснуте до правого нижнього кута.
    private static (Point Centre, double Radius) BadgeCircle(int sizePx)
    {
        var radius = Math.Max(4, Math.Round(sizePx * 0.25));
        return (new Point(sizePx - radius, sizePx - radius), radius);
    }

    /// <summary>Червоне коло з білою рискою навскіс: на 16 px читається краще за крихітний перекреслений мікрофон.</summary>
    private static void DrawBadge(DrawingContext context, int sizePx, bool lightTaskbar)
    {
        var (centre, radius) = BadgeCircle(sizePx);
        var red = new SolidColorBrush(lightTaskbar ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x6B, 0x6B));
        red.Freeze();
        context.DrawEllipse(red, null, centre, radius, radius);

        var pen = new Pen(Brushes.White, Math.Max(1.5, radius / 3.5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var reach = radius * 0.55;
        context.DrawLine(pen, new Point(centre.X - reach, centre.Y + reach), new Point(centre.X + reach, centre.Y - reach));
    }

    private static string CharOf(TrayGlyph glyph) => glyph switch
    {
        TrayGlyph.Muted => "",
        TrayGlyph.Volume0 => "",
        TrayGlyph.Volume1 => "",
        TrayGlyph.Volume2 => "",
        _ => "",
    };

    /// <summary>Гліф по центру квадрата: розмір підганяється під справжні контури, а не під рамку шрифту.</summary>
    private static void DrawGlyph(DrawingContext context, string text, int size, Brush brush)
    {
        var typeface = new Typeface(new FontFamily(IconFont), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Format(double emSize) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, emSize, brush, 1.0);

        var probe = Format(size).BuildGeometry(new Point(0, 0)).Bounds;
        var emSize = probe.IsEmpty || probe.Width <= 0 || probe.Height <= 0
            ? size * 0.85
            : size * (size * 0.9 / Math.Max(probe.Width, probe.Height));
        var final = Format(emSize);
        var bounds = final.BuildGeometry(new Point(0, 0)).Bounds;
        var origin = bounds.IsEmpty
            ? new Point((size - final.Width) / 2, (size - final.Height) / 2)
            : new Point((size - bounds.Width) / 2 - bounds.X, (size - bounds.Height) / 2 - bounds.Y);
        context.DrawText(final, origin);
    }

    private static System.Drawing.Icon ToIcon(BitmapSource source, int size)
    {
        var stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        // WPF дає попередньо помножені на прозорість кольори, а Bitmap Format32bppArgb чекає звичайні.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255) continue;
            for (var channel = 0; channel < 3; channel++)
                pixels[i + channel] = (byte)Math.Min(255, pixels[i + channel] * 255 / alpha);
        }

        using var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < size; row++)
                Marshal.Copy(pixels, row * stride, data.Scan0 + row * data.Stride, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
