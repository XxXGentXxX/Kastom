using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Hotkeys;

/// <summary>Одна клавіша на схемі: віртуальний код, напис на кеапі й розташування в одиницях клавіші.</summary>
public sealed record KeyboardKey(
    int VirtualKey,
    string Label,
    double X,
    double Y,
    double Width = 1,
    double Height = 1,
    HotkeyModifiers Modifier = HotkeyModifiers.None);

/// <summary>Статична модель повнорозмірної клавіатури ANSI з додатковою клавішею ISO (OEM102).</summary>
public static class KeyboardLayout
{
    /// <summary>Розмір схеми в одиницях клавіші.</summary>
    public const double TotalWidth = 23;

    public const double TotalHeight = 6.5;

    public static IReadOnlyList<KeyboardKey> Keys { get; } = Build();

    /// <summary>Чи використовує поєднання цю клавішу: як основну або як один з модифікаторів (лівий чи правий).</summary>
    public static bool Uses(HotkeyGesture gesture, KeyboardKey key) =>
        key.Modifier != HotkeyModifiers.None
            ? gesture.Modifiers.HasFlag(key.Modifier)
            : key.VirtualKey == gesture.VirtualKey;

    private static List<KeyboardKey> Build()
    {
        var keys = new List<KeyboardKey>();

        void Add(int vk, string label, double x, double y, double w = 1, double h = 1, HotkeyModifiers modifier = HotkeyModifiers.None) =>
            keys.Add(new KeyboardKey(vk, label, x, y, w, h, modifier));

        // Функціональний ряд.
        Add(0x1B, "Esc", 0, 0);
        for (var i = 0; i < 12; i++)
        {
            var group = i / 4;
            Add(0x70 + i, $"F{i + 1}", 2 + i + group * 0.5, 0);
        }

        Add(0x2C, "PrtSc", 15.5, 0);
        Add(0x91, "ScrLk", 16.5, 0);
        Add(0x13, "Pause", 17.5, 0);

        // Ряд цифр.
        Add(0xC0, "`", 0, 1.5);
        var digits = "1234567890";
        for (var i = 0; i < digits.Length; i++) Add(digits[i], digits[i].ToString(), 1 + i, 1.5);
        Add(0xBD, "-", 11, 1.5);
        Add(0xBB, "=", 12, 1.5);
        Add(0x08, "Backspace", 13, 1.5, 2);

        // Ряд QWERTY.
        Add(0x09, "Tab", 0, 2.5, 1.5);
        AddRow(keys, "QWERTYUIOP", 1.5, 2.5);
        Add(0xDB, "[", 11.5, 2.5);
        Add(0xDD, "]", 12.5, 2.5);
        Add(0xDC, "\\", 13.5, 2.5, 1.5);

        // Ряд ASDF.
        Add(0x14, "Caps", 0, 3.5, 1.75);
        AddRow(keys, "ASDFGHJKL", 1.75, 3.5);
        Add(0xBA, ";", 10.75, 3.5);
        Add(0xDE, "'", 11.75, 3.5);
        Add(0x0D, "Enter", 12.75, 3.5, 2.25);

        // Ряд Shift; між лівим Shift і Z — додаткова клавіша ISO.
        Add(0xA0, "Shift", 0, 4.5, 1.25, modifier: HotkeyModifiers.Shift);
        Add(0xE2, "\\", 1.25, 4.5);
        AddRow(keys, "ZXCVBNM", 2.25, 4.5);
        Add(0xBC, ",", 9.25, 4.5);
        Add(0xBE, ".", 10.25, 4.5);
        Add(0xBF, "/", 11.25, 4.5);
        Add(0xA1, "Shift", 12.25, 4.5, 2.75, modifier: HotkeyModifiers.Shift);

        // Нижній ряд.
        Add(0xA2, "Ctrl", 0, 5.5, 1.25, modifier: HotkeyModifiers.Control);
        Add(0x5B, "Win", 1.25, 5.5, 1.25, modifier: HotkeyModifiers.Win);
        Add(0xA4, "Alt", 2.5, 5.5, 1.25, modifier: HotkeyModifiers.Alt);
        Add(0x20, "Space", 3.75, 5.5, 6.25);
        Add(0xA5, "Alt", 10, 5.5, 1.25, modifier: HotkeyModifiers.Alt);
        Add(0x5C, "Win", 11.25, 5.5, 1.25, modifier: HotkeyModifiers.Win);
        Add(0x5D, "Menu", 12.5, 5.5, 1.25);
        Add(0xA3, "Ctrl", 13.75, 5.5, 1.25, modifier: HotkeyModifiers.Control);

        // Навігаційний блок і стрілки.
        Add(0x2D, "Ins", 15.5, 1.5);
        Add(0x24, "Home", 16.5, 1.5);
        Add(0x21, "PgUp", 17.5, 1.5);
        Add(0x2E, "Del", 15.5, 2.5);
        Add(0x23, "End", 16.5, 2.5);
        Add(0x22, "PgDn", 17.5, 2.5);
        Add(0x26, "↑", 16.5, 4.5);
        Add(0x25, "←", 15.5, 5.5);
        Add(0x28, "↓", 16.5, 5.5);
        Add(0x27, "→", 17.5, 5.5);

        // Цифровий блок.
        Add(0x90, "NumLk", 19, 1.5);
        Add(0x6F, "/", 20, 1.5);
        Add(0x6A, "*", 21, 1.5);
        Add(0x6D, "-", 22, 1.5);
        Add(0x67, "7", 19, 2.5);
        Add(0x68, "8", 20, 2.5);
        Add(0x69, "9", 21, 2.5);
        Add(0x6B, "+", 22, 2.5, 1, 2);
        Add(0x64, "4", 19, 3.5);
        Add(0x65, "5", 20, 3.5);
        Add(0x66, "6", 21, 3.5);
        Add(0x61, "1", 19, 4.5);
        Add(0x62, "2", 20, 4.5);
        Add(0x63, "3", 21, 4.5);
        Add(0x0D, "Enter", 22, 4.5, 1, 2); // той самий VK_RETURN, що й основний Enter
        Add(0x60, "0", 19, 5.5, 2);
        Add(0x6E, ".", 21, 5.5);

        return keys;
    }

    // Літерні клавіші: віртуальний код збігається з кодом великої літери.
    private static void AddRow(List<KeyboardKey> keys, string letters, double startX, double y)
    {
        for (var i = 0; i < letters.Length; i++)
            keys.Add(new KeyboardKey(letters[i], letters[i].ToString(), startX + i, y));
    }
}
