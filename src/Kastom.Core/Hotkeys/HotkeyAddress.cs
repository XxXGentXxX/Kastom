using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Hotkeys;

/// <summary>
/// Адреса розділу клавіш (Г2): непорожній набір модифікаторів, спільний для всіх дій розділу.
/// Поєднання дії = адреса розділу + її клавіша.
/// </summary>
public static class HotkeyAddress
{
    private const HotkeyModifiers All = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    /// <summary>Адреса розділу, дії якого не мають типових клавіш.</summary>
    public const HotkeyModifiers Fallback = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Адреси, які пропонує список вибору: спершу пари, потім три клавіші, потім одиночні.</summary>
    public static IReadOnlyList<HotkeyModifiers> Presets { get; } =
    [
        CtrlAlt,
        HotkeyModifiers.Control | HotkeyModifiers.Shift,
        HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        HotkeyModifiers.Control | HotkeyModifiers.Win,
        HotkeyModifiers.Alt | HotkeyModifiers.Win,
        HotkeyModifiers.Shift | HotkeyModifiers.Win,
        Fallback,
        HotkeyModifiers.Control,
        HotkeyModifiers.Alt,
        HotkeyModifiers.Win,
    ];

    /// <summary>Непорожній набір з клавіш Ctrl, Alt, Shift, Win.</summary>
    public static bool IsValid(HotkeyModifiers address) => address != HotkeyModifiers.None && (address & ~All) == 0;

    /// <summary>Одна клавіша-модифікатор забирає звичайні скорочення програм (Ctrl+M, Alt+Tab…).</summary>
    public static bool IsSingle(HotkeyModifiers address) => IsValid(address) && (address & (address - 1)) == 0;

    /// <summary>AltGr у Windows — це Ctrl+Alt, тож літери з AltGr (в українській розкладці U дає «ґ») не годяться для дій.</summary>
    public static bool IsCtrlAlt(HotkeyModifiers address) => address == CtrlAlt;

    /// <summary>Текст на кшталт "Ctrl+Alt"; порядок той самий, що в <see cref="HotkeyGesture.ToString"/>.</summary>
    public static string Format(HotkeyModifiers address)
    {
        var parts = new List<string>(4);
        if (address.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (address.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (address.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (address.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out HotkeyModifiers address)
    {
        address = HotkeyModifiers.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": address |= HotkeyModifiers.Control; break;
                case "alt": address |= HotkeyModifiers.Alt; break;
                case "shift": address |= HotkeyModifiers.Shift; break;
                case "win" or "windows": address |= HotkeyModifiers.Win; break;
                default:
                    address = HotkeyModifiers.None;
                    return false;
            }
        }

        return IsValid(address);
    }

    /// <summary>
    /// Найчастіший непорожній набір серед переданих; при рівності — той, що трапився раніше.
    /// Null, якщо непорожніх наборів немає.
    /// </summary>
    public static HotkeyModifiers? MostCommon(IEnumerable<HotkeyModifiers> sets)
    {
        var counts = new List<(HotkeyModifiers Set, int Count)>();
        foreach (var set in sets)
        {
            if (!IsValid(set)) continue;
            var index = counts.FindIndex(c => c.Set == set);
            if (index < 0) counts.Add((set, 1));
            else counts[index] = (set, counts[index].Count + 1);
        }

        if (counts.Count == 0) return null;
        var best = counts[0];
        foreach (var candidate in counts.Skip(1))
        {
            if (candidate.Count > best.Count) best = candidate;
        }

        return best.Set;
    }

    /// <summary>Типова адреса розділу: найчастіші модифікатори типових клавіш його дій, інакше <see cref="Fallback"/>.</summary>
    public static HotkeyModifiers DefaultFor(IEnumerable<HotkeyModifiers> defaultSets) => MostCommon(defaultSets) ?? Fallback;
}
