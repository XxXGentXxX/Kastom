using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kastom.Modules.Audio.Tray;

/// <summary>Що намальовано на значку в області сповіщень.</summary>
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

    Mic,

    /// <summary>Мікрофон вимкнено: червоний з перекресленням.</summary>
    MicOff,
}

/// <summary>Малює значки звуку й мікрофона гліфами Segoe Fluent Icons під колір панелі задач.</summary>
internal static class TrayIconRenderer
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static System.Drawing.Icon Render(TrayGlyph glyph, bool lightTaskbar, int sizePx)
    {
        var brush = new SolidColorBrush(ColorOf(glyph, lightTaskbar));
        brush.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            DrawGlyph(context, CharOf(glyph), sizePx, brush);
            if (glyph == TrayGlyph.MicOff)
            {
                var pen = new Pen(brush, sizePx / 10.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                context.DrawLine(pen, new Point(sizePx * 0.18, sizePx * 0.12), new Point(sizePx * 0.82, sizePx * 0.88));
            }
        }

        var target = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return ToIcon(target, sizePx);
    }

    private static Color ColorOf(TrayGlyph glyph, bool lightTaskbar)
    {
        if (glyph == TrayGlyph.MicOff)
            return lightTaskbar ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x6B, 0x6B);
        return lightTaskbar ? Colors.Black : Colors.White;
    }

    private static string CharOf(TrayGlyph glyph) => glyph switch
    {
        TrayGlyph.Muted => "",
        TrayGlyph.Volume0 => "",
        TrayGlyph.Volume1 => "",
        TrayGlyph.Volume2 => "",
        TrayGlyph.Volume3 => "",
        _ => "",
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
