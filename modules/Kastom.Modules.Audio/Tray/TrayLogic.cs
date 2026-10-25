using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Tray;

/// <summary>Що має показувати значок: чи видно його, який гліф, чи є позначка вимкненого мікрофона і яка підказка.</summary>
internal readonly record struct TrayIconState(bool Visible, TrayGlyph Glyph, bool MicBadge, string Tooltip);

/// <summary>Вигляд, під який намальовано <see cref="System.Drawing.Icon"/>: інший гліф, позначка, тема чи розмір — інша картинка.</summary>
internal readonly record struct TrayLook(TrayGlyph Glyph, bool MicBadge, bool Light, int Size);

/// <summary>
/// Рішення про значок без жодного NotifyIcon, щоб їх можна було перевірити тестами: що показати, коли перемальовувати,
/// як не дати помилці обробника вбити лаунчер.
/// </summary>
internal static class TrayLogic
{
    // Windows не приймає довшу підказку.
    internal const int MaxTooltipLength = 127;

    internal static string FitTooltip(string text) => Cut(text, MaxTooltipLength);

    /// <summary>
    /// Два рядки підказки (звук, мікрофон) у межах, які приймає Windows. Задовгі рядки обрізаються так, щоб короткий
    /// рядок не втрачав нічого, а довгий не витіснив сусіда повністю.
    /// </summary>
    internal static string FitTooltip(string first, string second)
    {
        var budget = MaxTooltipLength - 1;
        if (first.Length + second.Length > budget)
        {
            first = Cut(first, Math.Max(budget / 2, budget - second.Length));
            second = Cut(second, budget - first.Length);
        }

        return first + "\n" + second;
    }

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

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

    /// <summary>Другий рядок підказки: стан і назва мікрофона за замовчуванням.</summary>
    internal static string DescribeMic(AudioDevice mic) =>
        Strings.Format(mic.IsMuted ? Strings.TrayMicOffTooltipFormat : Strings.TrayMicOnTooltipFormat, mic.Name);

    /// <summary>
    /// Єдиний значок. Він є, поки його не вимкнено в налаштуваннях; без пристрою виводу показує «немає пристрою».
    /// Підказка — два рядки (звук і мікрофон; без мікрофона лише звук). Позначка є, коли мікрофон вимкнено й її ввімкнено.
    /// </summary>
    internal static TrayIconState Icon(bool enabled, bool badgeEnabled, AudioDevice? output, AudioDevice? mic)
    {
        var (glyph, sound) = DescribeSound(output);
        var tooltip = mic is null ? FitTooltip(sound) : FitTooltip(sound, DescribeMic(mic));
        return new TrayIconState(enabled, glyph, badgeEnabled && mic?.IsMuted == true, tooltip);
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
