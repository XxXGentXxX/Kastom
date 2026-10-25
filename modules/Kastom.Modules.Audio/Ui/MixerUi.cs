using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Ключі пензлів теми Fluent лаунчера: кольорів у коді немає, лише посилання на ресурси.</summary>
internal static class ThemeKeys
{
    public const string TextPrimary = "TextFillColorPrimaryBrush";
    public const string TextSecondary = "TextFillColorSecondaryBrush";
    public const string TextOnAccent = "TextOnAccentFillColorPrimaryBrush";
    public const string Critical = "SystemFillColorCriticalBrush";
    public const string Accent = "AccentFillColorDefaultBrush";
    public const string SolidBackground = "SolidBackgroundFillColorBaseBrush";
    public const string Divider = "DividerStrokeColorDefaultBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";
    public const string CardBackground = "CardBackgroundFillColorDefaultBrush";
    public const string Subtle = "SubtleFillColorSecondaryBrush";
}

/// <summary>Гліфи шрифту значків Windows (Segoe Fluent Icons, на Windows 10 — Segoe MDL2 Assets).</summary>
internal static class Glyphs
{
    public const string Speaker = "";
    public const string Headphones = "";
    public const string Microphone = "";
    public const string Muted = "";
    public const string More = "";
    public const string Settings = "";
    public const string App = "";
    public const string SystemSounds = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";

    public static string Volume(int percent) =>
        percent < 1 ? "" : percent < 33 ? "" : percent < 66 ? "" : "";

    public static string ForOutput(string deviceName) =>
        deviceName.Contains("навушник", StringComparison.CurrentCultureIgnoreCase)
        || deviceName.Contains("headphone", StringComparison.OrdinalIgnoreCase)
        || deviceName.Contains("headset", StringComparison.OrdinalIgnoreCase)
            ? Headphones
            : Speaker;
}

/// <summary>Дрібні конструктори елементів мікшера.</summary>
internal static class MixerUi
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Text(string text, double size, bool secondary = false, bool semibold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? ThemeKeys.TextSecondary : ThemeKeys.TextPrimary);
        return block;
    }

    public static TextBlock Glyph(string glyph, double size)
    {
        var block = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);

        return block;
    }

    /// <summary>У заголовку пункту меню "_" означає гарячу літеру, тож у назвах пристроїв її подвоюємо.</summary>
    public static string EscapeMenu(string text) => text.Replace("_", "__");

    public static string Percent(double value) =>
        MixerStrings.Format(MixerStrings.PercentFormat, Math.Clamp((int)Math.Round(value), 0, 100));
}
