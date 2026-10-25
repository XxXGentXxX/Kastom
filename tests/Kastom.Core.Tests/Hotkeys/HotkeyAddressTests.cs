using Kastom.Core.Abstractions;
using Kastom.Core.Hotkeys;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Tests.Hotkeys;

public class HotkeyAddressTests
{
    private const HotkeyModifiers C = HotkeyModifiers.Control;
    private const HotkeyModifiers A = HotkeyModifiers.Alt;
    private const HotkeyModifiers S = HotkeyModifiers.Shift;
    private const HotkeyModifiers W = HotkeyModifiers.Win;

    [Fact]
    public void Presets_are_all_valid_and_distinct()
    {
        Assert.All(HotkeyAddress.Presets, preset => Assert.True(HotkeyAddress.IsValid(preset)));
        Assert.Equal(HotkeyAddress.Presets.Count, HotkeyAddress.Presets.Distinct().Count());
        Assert.Equal(7, HotkeyAddress.Presets.Count(preset => !HotkeyAddress.IsSingle(preset)));
        Assert.Equal([C, A, W], HotkeyAddress.Presets.Where(HotkeyAddress.IsSingle));
    }

    [Theory]
    [InlineData("Ctrl+Alt", C | A)]
    [InlineData("alt + control", C | A)]
    [InlineData("Win+Shift", S | W)]
    [InlineData("Ctrl", C)]
    [InlineData("Ctrl+Alt+Shift", C | A | S)]
    public void Parse_and_format_round_trip(string text, HotkeyModifiers expected)
    {
        Assert.True(HotkeyAddress.TryParse(text, out var address));
        Assert.Equal(expected, address);
        Assert.True(HotkeyAddress.TryParse(HotkeyAddress.Format(address), out var again));
        Assert.Equal(expected, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+M")]
    [InlineData("Ctrl+Nonsense")]
    public void Parse_rejects_empty_and_non_modifier_text(string? text)
    {
        Assert.False(HotkeyAddress.TryParse(text, out _));
    }

    [Fact]
    public void Most_common_prefers_higher_count_then_earlier_and_ignores_empty_sets()
    {
        Assert.Equal(C | S, HotkeyAddress.MostCommon([C | A, C | S, C | S]));
        Assert.Equal(C | A, HotkeyAddress.MostCommon([C | A, C | S]));
        Assert.Equal(C, HotkeyAddress.MostCommon([HotkeyModifiers.None, C]));
        Assert.Null(HotkeyAddress.MostCommon([HotkeyModifiers.None]));
        Assert.Equal(HotkeyAddress.Fallback, HotkeyAddress.DefaultFor([]));
    }
}

public partial class HotkeyManagerTests
{
    [Fact]
    public void SetAddress_reregisters_every_action_of_the_section_only()
    {
        using var f = Create(
            Action("audio", "mute", CtrlAltM, "Звук"),
            Action("audio", "next", CtrlAltN, "Звук"),
            Action("video", "cam", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'C'), "Відео"));
        var videoId = f.Backend.Registered.Single(p => p.Value.VirtualKey == 'C').Key;

        var result = f.Manager.SetAddress("audio", HotkeyModifiers.Control | HotkeyModifiers.Win);

        Assert.True(result.Success);
        Assert.Equal("Ctrl+Win", f.State.State.HotkeyAddresses["audio"]);
        Assert.Equal("Ctrl+Win+M", f.Binding("audio/mute").Gesture.ToString());
        Assert.Equal("Ctrl+Win+N", f.Binding("audio/next").Gesture.ToString());
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
        Assert.Equal(3, f.Backend.Registered.Count);
        Assert.DoesNotContain(CtrlAltM, f.Backend.Registered.Values);
        Assert.Equal('C', f.Backend.Registered[videoId].VirtualKey);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Win, f.Manager.Sections.Single(s => s.Id == "audio").Address);
    }

    [Fact]
    public void SetAddress_with_conflicts_is_accepted_and_the_rest_still_works()
    {
        using var f = Create(
            Action("audio", "mute", CtrlAltM, "Звук"),
            Action("audio", "next", CtrlAltN, "Звук"),
            Action("video", "mute", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'M'), "Фільми"),
            Action("video", "cam", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'C'), "Фільми"));
        f.Backend.Taken.Add(new HotkeyGesture(CtrlAlt, 'C'));

        var result = f.Manager.SetAddress("video", CtrlAlt);

        Assert.True(result.Success);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
        Assert.Equal(HotkeyBindingStatus.ConflictInternal, f.Binding("video/mute").Status);
        Assert.Equal(CtrlAltM, f.Binding("video/mute").Gesture);
        Assert.Equal(HotkeyBindingStatus.TakenBySystem, f.Binding("video/cam").Status);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/next").Status);
        Assert.Equal([CtrlAltM, CtrlAltN], f.Backend.Registered.Values.OrderBy(g => g.VirtualKey));
    }

    [Fact]
    public void SetAddress_rejects_unknown_section_and_empty_address()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        Assert.False(f.Manager.SetAddress("nope", CtrlAlt).Success);
        Assert.False(f.Manager.SetAddress("audio", (HotkeyModifiers)0x10).Success);
        Assert.Empty(f.State.State.HotkeyAddresses);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public void ResetAddress_returns_the_default_address()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.SetAddress("audio", HotkeyModifiers.Alt | HotkeyModifiers.Shift);

        var result = f.Manager.ResetAddress("audio");

        Assert.True(result.Success);
        Assert.Empty(f.State.State.HotkeyAddresses);
        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.True(f.Manager.Sections.Single().IsDefaultAddress);
    }

    [Fact]
    public void Chosen_address_survives_a_restart_and_changes_defaults_issued_later()
    {
        using var first = Create(Action("audio", "mute", CtrlAltM));
        first.Manager.SetAddress("audio", HotkeyModifiers.Control | HotkeyModifiers.Shift);

        using var second = new Fixture();
        foreach (var (key, value) in first.State.State.HotkeyAddresses) second.State.State.HotkeyAddresses[key] = value;
        foreach (var (key, value) in first.State.State.HotkeyKeys) second.State.State.HotkeyKeys[key] = value;
        second.Catalog.Add(Action("audio", "mute", CtrlAltM));
        second.Catalog.Add(Action("audio", "next", CtrlAltN));
        second.Start();

        Assert.Equal("Ctrl+Shift+M", second.Binding("audio/mute").Gesture.ToString());
        Assert.Equal("Ctrl+Shift+N", second.Binding("audio/next").Gesture.ToString());
        Assert.Equal(HotkeyBindingStatus.Active, second.Binding("audio/next").Status);
    }

    [Fact]
    public void Unusable_stored_address_falls_back_to_default_and_is_logged()
    {
        using var f = new Fixture();
        f.State.State.HotkeyAddresses["audio"] = "Ctrl+Nonsense";
        f.Catalog.Add(Action("audio", "mute", CtrlAltM));
        f.Start();

        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.NotEmpty(f.Log.Warnings);
    }

    [Fact]
    public void SetAddress_save_failure_reverts_address_and_registrations()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.State.ThrowOnSave = true;

        var result = f.Manager.SetAddress("audio", HotkeyModifiers.Control | HotkeyModifiers.Shift);

        Assert.False(result.Success);
        Assert.Empty(f.State.State.HotkeyAddresses);
        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.Equal([CtrlAltM], f.Backend.Registered.Values);
    }

    [Fact]
    public void Disabled_switch_keeps_effective_gestures_for_every_section()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM), Action("video", "cam", CtrlAltN));
        f.Manager.SetAddress("video", HotkeyModifiers.Alt | HotkeyModifiers.Shift);

        f.Manager.Enabled = false;

        Assert.Empty(f.Backend.Registered);
        Assert.Equal(HotkeyBindingStatus.Disabled, f.Binding("video/cam").Status);
        Assert.Equal("Alt+Shift+N", f.Binding("video/cam").Gesture.ToString());
    }

    [Fact]
    public void SetKey_clearing_and_ResetKey_work_with_a_custom_address()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.SetAddress("audio", HotkeyModifiers.Control | HotkeyModifiers.Shift);

        Assert.True(f.Manager.SetKey("audio/mute", null).Success);
        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/mute").Status);

        Assert.True(f.Manager.ResetKey("audio/mute").Success);
        Assert.Equal("Ctrl+Shift+M", f.Binding("audio/mute").Gesture.ToString());
        Assert.Equal("M", f.State.State.HotkeyKeys["audio/mute"]);
    }

    [Fact]
    public void SetKey_unknown_key_is_rejected()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        Assert.False(f.Manager.SetKey("audio/mute", 0x07).Success);
        Assert.Equal("M", f.State.State.HotkeyKeys["audio/mute"]);
    }
}
