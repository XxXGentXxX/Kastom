using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Tests.Hotkeys;

public class KeyNamesTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D)]
    [InlineData("Shift+Win+S", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x53)]
    [InlineData("Ctrl+F5", HotkeyModifiers.Control, 0x74)]
    [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, 0x2C)]
    [InlineData("Ctrl+Shift+NumPlus", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x6B)]
    [InlineData("Ctrl+Alt+Shift+Win+Delete", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x2E)]
    [InlineData("Ctrl+;", HotkeyModifiers.Control, 0xBA)]
    [InlineData("Ctrl+\\", HotkeyModifiers.Control, 0xDC)]
    [InlineData("Ctrl+OEM102", HotkeyModifiers.Control, 0xE2)]
    [InlineData("VolumeMute", HotkeyModifiers.None, 0xAD)]
    public void Parse_gives_expected_gesture_and_round_trips(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, virtualKey), gesture);
        Assert.Equal(text, gesture.ToString());
    }

    [Fact]
    public void Parse_is_case_insensitive_and_accepts_aliases_and_any_modifier_order()
    {
        Assert.True(HotkeyGesture.TryParse("alt + control + m", out var gesture));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'M'), gesture);
        Assert.Equal("Ctrl+Alt+M", gesture.ToString());

        Assert.True(HotkeyGesture.TryParse("Windows+E", out var win));
        Assert.Equal(HotkeyModifiers.Win, win.Modifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Nonsense")]
    public void Parse_rejects_invalid_text(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Every_named_non_modifier_key_round_trips()
    {
        foreach (var (virtualKey, name) in KeyNames.All)
        {
            var gesture = new HotkeyGesture(HotkeyModifiers.Control, virtualKey);
            Assert.True(gesture.IsValid, name);
            Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var parsed), name);
            Assert.Equal(gesture, parsed);
        }
    }

    [Fact]
    public void Names_are_unique()
    {
        Assert.Equal(KeyNames.All.Count, KeyNames.All.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0xA2)]
    [InlineData(0xA5)]
    public void Modifier_keys_are_not_valid_as_main_key(int virtualKey)
    {
        Assert.True(KeyNames.IsModifierKey(virtualKey));
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, virtualKey).IsValid);
    }

    [Fact]
    public void Unknown_key_is_invalid_and_formats_as_hex()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control, 0xFF);

        Assert.False(gesture.IsValid);
        Assert.Equal("Ctrl+0xFF", gesture.ToString());
    }

    [Theory]
    [InlineData(0x41, true)]
    [InlineData(0x31, true)]
    [InlineData(0x20, true)]
    [InlineData(0x70, false)]
    [InlineData(0x87, false)]
    [InlineData(0xAD, false)]
    [InlineData(0xB3, false)]
    public void RequiresModifier_exempts_function_and_media_keys(int virtualKey, bool expected)
    {
        Assert.Equal(expected, KeyNames.RequiresModifier(virtualKey));
    }
}
