namespace Kastom.Sdk.Hotkeys;

/// <summary>Клавіші-модифікатори. Значення збігаються з MOD_* у Win32 RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Поєднання клавіш: модифікатори й одна звичайна клавіша (віртуальний код Windows, VK_*).</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>Клавіша відома й сама не є модифікатором.</summary>
    public bool IsValid => !KeyNames.IsModifierKey(VirtualKey) && KeyNames.TryGetName(VirtualKey, out _);

    /// <summary>Текст на кшталт "Ctrl+Alt+M"; так поєднання і зберігається, і показується.</summary>
    public override string ToString() => KeyNames.Format(this);

    public static bool TryParse(string? text, out HotkeyGesture gesture) => KeyNames.TryParse(text, out gesture);
}

/// <summary>Назви клавіш для збереження й показу поєднань.</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = Build();

    private static readonly Dictionary<string, int> KeysByName =
        Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<int, string> All => Names;

    public static bool TryGetName(int virtualKey, out string name) => Names.TryGetValue(virtualKey, out name!);

    public static bool TryGetKey(string name, out int virtualKey) => KeysByName.TryGetValue(name, out virtualKey);

    /// <summary>Shift, Ctrl, Alt, Win у будь-якому варіанті (лівий, правий, загальний).</summary>
    public static bool IsModifierKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>
    /// Чи потрібен модифікатор. Без модифікатора дозволені лише F1–F24 і медіаклавіші:
    /// поєднання з самої літери забрало б її в усіх програмах.
    /// </summary>
    public static bool RequiresModifier(int virtualKey) =>
        virtualKey is not ((>= 0x70 and <= 0x87) or (>= 0xAD and <= 0xB3));

    public static string Format(HotkeyGesture gesture)
    {
        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(TryGetName(gesture.VirtualKey, out var name) ? name : $"0x{gesture.VirtualKey:X2}");
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; continue;
                case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": modifiers |= HotkeyModifiers.Win; continue;
            }

            if (key is not null || !TryGetKey(part, out var virtualKey)) return false;
            key = virtualKey;
        }

        if (key is null) return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
        return gesture.IsValid;
    }

    private static Dictionary<int, string> Build()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
            [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End",
            [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x6A] = "NumMultiply", [0x6B] = "NumPlus", [0x6D] = "NumMinus", [0x6E] = "NumDecimal",
            [0x6F] = "NumDivide", [0x90] = "NumLock", [0x91] = "ScrollLock",
            [0xAD] = "VolumeMute", [0xAE] = "VolumeDown", [0xAF] = "VolumeUp", [0xB0] = "MediaNext",
            [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlayPause",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "OEM102",
        };
        for (var digit = 0; digit <= 9; digit++)
        {
            names[0x30 + digit] = digit.ToString();
            names[0x60 + digit] = $"Num{digit}";
        }

        for (var letter = 'A'; letter <= 'Z'; letter++) names[letter] = letter.ToString();
        for (var f = 1; f <= 24; f++) names[0x6F + f] = $"F{f}";
        return names;
    }
}
