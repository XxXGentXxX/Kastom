using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Tray;

/// <summary>Що має показувати один значок: чи видно його, який гліф і яка підказка.</summary>
internal readonly record struct TrayIconState(bool Visible, TrayGlyph Glyph, string Tooltip);

/// <summary>Вигляд, під який намальовано <see cref="System.Drawing.Icon"/>: інший гліф, тема чи розмір — інша картинка.</summary>
internal readonly record struct TrayLook(TrayGlyph Glyph, bool Light, int Size);

/// <summary>
/// Рішення про значки без жодного NotifyIcon, щоб їх можна було перевірити тестами: що показати, коли перемальовувати,
/// як не дати помилці обробника вбити лаунчер.
/// </summary>
internal static class TrayLogic
{
    // Windows не приймає довшу підказку.
    internal const int MaxTooltipLength = 127;

    internal static string FitTooltip(string text) => text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;

    internal static (TrayGlyph Glyph, string Text) DescribeSound(AudioDevice? output)
    {
        if (output is null) return (TrayGlyph.Muted, Strings.TrayNoDevice);
        if (output.IsMuted) return (TrayGlyph.Muted, Strings.Format(Strings.TrayMutedTooltipFormat, output.Name));

        var percent = VolumeMath.ToPercent(output.Volume);
        var glyph = percent < 1 ? TrayGlyph.Volume0
            : percent < 33 ? TrayGlyph.Volume1
            : percent < 66 ? TrayGlyph.Volume2
            : TrayGlyph.Volume3;
        return (glyph, Strings.Format(Strings.TrayVolumeTooltipFormat, output.Name, percent));
    }

    internal static (TrayGlyph Glyph, string Text) DescribeMic(AudioDevice mic) =>
        (mic.IsMuted ? TrayGlyph.MicOff : TrayGlyph.Mic,
            Strings.Format(mic.IsMuted ? Strings.TrayMicOffTooltipFormat : Strings.TrayMicOnTooltipFormat, mic.Name));

    /// <summary>Значок звуку є, поки його не вимкнено в налаштуваннях; без пристрою виводу він показує «немає пристрою».</summary>
    internal static TrayIconState Sound(bool enabled, AudioDevice? output)
    {
        var (glyph, text) = DescribeSound(output);
        return new TrayIconState(enabled, glyph, FitTooltip(text));
    }

    /// <summary>Значок мікрофона є, коли його не вимкнено в налаштуваннях і є мікрофон за замовчуванням.</summary>
    internal static TrayIconState Mic(bool enabled, AudioDevice? mic)
    {
        if (mic is null) return new TrayIconState(false, TrayGlyph.Mic, "");

        var (glyph, text) = DescribeMic(mic);
        return new TrayIconState(enabled, glyph, FitTooltip(text));
    }

    /// <summary>Icon малюємо наново лише тоді, коли вигляд справді змінився (гучність 45 → 46 % гліф не міняє).</summary>
    internal static bool NeedsRedraw(TrayLook? drawn, TrayLook wanted) => drawn != wanted;

    internal static bool NeedsTooltip(string? current, string wanted) => current != wanted;

    /// <summary>
    /// Виконати обробник кліку чи пункту меню. У лаунчера немає Application.ThreadException: виняток із WinForms-обробника
    /// вбив би процес, тож він стає записом у лог і підказкою на екрані.
    /// </summary>
    internal static void RunGuarded(IAudioShell shell, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                shell.ReportError(Strings.OsdCommandFailed, ex);
            }
            catch
            {
                // Повідомлення про помилку саме не має ламати обробник події.
            }
        }
    }
}
