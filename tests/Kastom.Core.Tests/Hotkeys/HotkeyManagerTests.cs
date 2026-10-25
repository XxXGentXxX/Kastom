using Kastom.Core.Abstractions;
using Kastom.Core.Hotkeys;
using Kastom.Core.Models;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Services;

namespace Kastom.Core.Tests.Hotkeys;

public partial class HotkeyManagerTests
{
    private static readonly HotkeyGesture CtrlAltM = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'M');
    private static readonly HotkeyGesture CtrlAltN = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'N');
    private static readonly HotkeyGesture CtrlAltK = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'K');
    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    private sealed class Fixture : IDisposable
    {
        public FakeCatalog Catalog { get; } = new();
        public FakeStateStore State { get; } = new();
        public FakeBackend Backend { get; } = new();
        public FakeLogger Log { get; } = new();
        public HotkeyManager Manager { get; private set; } = null!;

        private int _baseline;

        /// <summary>Записи стану після старту (запис типових поєднань під час створення не рахується).</summary>
        public int Saves => State.SaveCount - _baseline;

        public Fixture Start()
        {
            Manager = new HotkeyManager(Catalog, State, Backend, Log);
            _baseline = State.SaveCount;
            return this;
        }

        public HotkeyBindingInfo Binding(string globalId) =>
            Manager.Bindings.Single(b => b.Action.GlobalId == globalId);

        public void Dispose() => Manager.Dispose();
    }

    private static Fixture Create(params RegisteredAction[] actions)
    {
        var fixture = new Fixture();
        foreach (var action in actions) fixture.Catalog.Add(action);
        return fixture.Start();
    }

    private static RegisteredAction Action(string moduleId, string actionId, HotkeyGesture? defaultHotkey = null, string? moduleName = null) =>
        new(moduleId, moduleName ?? moduleId, new ModuleAction
        {
            Id = actionId,
            Title = actionId,
            DefaultHotkey = defaultHotkey,
            Execute = _ => Task.CompletedTask,
        });

    [Fact]
    public void Default_hotkey_is_applied_and_registered()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        var binding = f.Binding("audio/mute");
        Assert.Equal(CtrlAltM, binding.Gesture);
        Assert.Equal(HotkeyBindingStatus.Active, binding.Status);
        Assert.Single(f.Backend.Registered);
    }

    [Fact]
    public void Default_without_modifiers_takes_the_fallback_address()
    {
        using var f = Create(Action("audio", "mute", new HotkeyGesture(HotkeyModifiers.None, 0x7B)));

        var binding = f.Binding("audio/mute");
        Assert.Equal(new HotkeyGesture(HotkeyAddress.Fallback, 0x7B), binding.Gesture);
        Assert.Equal(HotkeyBindingStatus.Active, binding.Status);
        Assert.Equal(HotkeyAddress.Fallback, f.Manager.Sections.Single().Address);
    }

    [Fact]
    public void Section_without_default_keys_has_fallback_address_and_no_gesture()
    {
        using var f = Create(Action("audio", "mute"));

        Assert.Equal(HotkeyAddress.Fallback, f.Manager.Sections.Single().Address);
        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/mute").Status);
    }

    [Fact]
    public void Default_address_is_the_most_common_modifiers_of_default_keys()
    {
        using var f = Create(
            Action("video", "a", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'A')),
            Action("video", "b", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'B')),
            Action("video", "c", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'C')));

        var section = Assert.Single(f.Manager.Sections);
        Assert.Equal(CtrlAlt, section.Address);
        Assert.Equal(CtrlAlt, section.DefaultAddress);
        Assert.Equal(new HotkeyGesture(CtrlAlt, 'A'), f.Binding("video/a").Gesture);
    }

    [Fact]
    public void Default_already_used_by_earlier_binding_is_not_applied()
    {
        using var f = Create(
            Action(KastomSdk.LauncherModuleId, "open", CtrlAltM, "Лаунчер"),
            Action("audio", "mute", CtrlAltM, "Звук"));

        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("kastom/open").Status);
        var second = f.Binding("audio/mute");
        Assert.Null(second.Gesture);
        Assert.Equal(HotkeyBindingStatus.Unassigned, second.Status);
    }

    [Fact]
    public void Launcher_comes_first_then_modules_by_name()
    {
        using var f = Create(
            Action("zeta", "a", moduleName: "Яблуко"),
            Action("alpha", "a", moduleName: "Барвінок"),
            Action(KastomSdk.LauncherModuleId, "open", moduleName: "Лаунчер"));

        Assert.Equal(["kastom/open", "alpha/a", "zeta/a"], f.Manager.Bindings.Select(b => b.Action.GlobalId));
    }

    [Fact]
    public void Explicit_assignment_beats_default_of_another_action()
    {
        using var f = new Fixture();
        f.Catalog.Add(Action("audio", "mute", CtrlAltM));
        f.Catalog.Add(Action("video", "cam"));
        f.State.State.HotkeyAddresses["video"] = "Ctrl+Alt";
        f.State.State.HotkeyKeys["video/cam"] = "M";
        f.Start();

        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("video/cam").Status);
        var loser = f.Binding("audio/mute");
        Assert.Null(loser.Gesture);
        Assert.Equal(HotkeyBindingStatus.Unassigned, loser.Status);
    }

    [Fact]
    public void Unparsable_stored_gesture_means_none_and_is_logged()
    {
        using var f = new Fixture();
        f.Catalog.Add(Action("audio", "mute", CtrlAltM));
        f.State.State.HotkeyKeys["audio/mute"] = "Nonsense";
        f.Start();

        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/mute").Status);
        Assert.NotEmpty(f.Log.Warnings);
    }

    [Fact]
    public void SetKey_succeeds_saves_and_registers()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        var raised = 0;
        f.Manager.Changed += (_, _) => raised++;

        var result = f.Manager.SetKey("audio/mute", 'N');

        Assert.True(result.Success);
        Assert.Equal("N", f.State.State.HotkeyKeys["audio/mute"]);
        Assert.Equal(1, f.Saves);
        Assert.Equal(CtrlAltN, f.Binding("audio/mute").Gesture);
        Assert.Equal([CtrlAltN], f.Backend.Registered.Values);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetKey_same_gesture_again_succeeds()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        Assert.True(f.Manager.SetKey("audio/mute", 'M').Success);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
    }

    [Fact]
    public void SetKey_unknown_action_fails()
    {
        using var f = Create();

        Assert.False(f.Manager.SetKey("nope/none", 'M').Success);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public void SetAddress_without_modifiers_is_rejected()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        var result = f.Manager.SetAddress("audio", HotkeyModifiers.None);

        Assert.False(result.Success);
        Assert.Contains("модифікатор", result.Error);
        Assert.Empty(f.State.State.HotkeyAddresses);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public void SetKey_modifier_key_is_rejected()
    {
        using var f = Create(Action("audio", "mute"));

        Assert.False(f.Manager.SetKey("audio/mute", 0x11).Success);
    }

    [Fact]
    public void SetKey_internal_conflict_is_rejected_with_conflicting_action()
    {
        using var f = Create(
            Action("audio", "mute", CtrlAltM, "Звук"),
            Action("video", "cam"));
        f.Manager.SetAddress("video", CtrlAlt);
        var savesBefore = f.Saves;

        var result = f.Manager.SetKey("video/cam", 'M');

        Assert.False(result.Success);
        Assert.Equal("audio/mute", result.ConflictingActionId);
        Assert.Contains("«mute»", result.Error);
        Assert.Contains("Звук", result.Error);
        Assert.DoesNotContain("video/cam", f.State.State.HotkeyKeys.Keys);
        Assert.Equal(savesBefore, f.Saves);
    }

    [Fact]
    public void SetKey_conflicts_with_explicitly_stored_gesture()
    {
        using var f = new Fixture();
        f.Catalog.Add(Action("audio", "mute"));
        f.Catalog.Add(Action("video", "cam"));
        f.State.State.HotkeyKeys["audio/mute"] = "K";
        f.Start();

        var result = f.Manager.SetKey("video/cam", 'K');

        Assert.False(result.Success);
        Assert.Equal("audio/mute", result.ConflictingActionId);
    }

    [Fact]
    public void SetKey_taken_by_system_is_rejected_and_not_saved()
    {
        using var f = new Fixture();
        f.Backend.Taken.Add(CtrlAltN);
        f.State.State.HotkeyAddresses["audio"] = "Ctrl+Alt";
        f.Catalog.Add(Action("audio", "mute"));
        f.Start();

        var result = f.Manager.SetKey("audio/mute", 'N');

        Assert.False(result.Success);
        Assert.Null(result.ConflictingActionId);
        Assert.Empty(f.State.State.HotkeyKeys);
        Assert.Equal(0, f.Saves);
        Assert.Empty(f.Backend.Registered);
    }

    [Fact]
    public void SetKey_probe_does_not_leave_registration_behind()
    {
        using var f = Create(Action("audio", "mute"));

        Assert.True(f.Manager.SetKey("audio/mute", 'N').Success);

        Assert.Single(f.Backend.Registered);
        Assert.Equal(f.Backend.RegisterCalls.Count - f.Backend.Registered.Count, f.Backend.UnregisterCalls.Count);
    }

    [Fact]
    public void Default_refused_by_backend_gets_taken_by_system_status()
    {
        using var f = new Fixture();
        f.Backend.Taken.Add(CtrlAltM);
        f.Catalog.Add(Action("audio", "mute", CtrlAltM));
        f.Start();

        var binding = f.Binding("audio/mute");
        Assert.Equal(CtrlAltM, binding.Gesture);
        Assert.Equal(HotkeyBindingStatus.TakenBySystem, binding.Status);
        Assert.Empty(f.Backend.Registered);
    }

    [Fact]
    public void Two_stored_equal_gestures_give_internal_conflict_for_the_second()
    {
        using var f = new Fixture();
        f.Catalog.Add(Action("audio", "a"));
        f.Catalog.Add(Action("audio", "b"));
        f.State.State.HotkeyKeys["audio/a"] = "M";
        f.State.State.HotkeyKeys["audio/b"] = "M";
        f.Start();

        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/a").Status);
        Assert.Equal(HotkeyBindingStatus.ConflictInternal, f.Binding("audio/b").Status);
        Assert.Single(f.Backend.Registered);
    }

    [Fact]
    public void SetKey_null_stores_empty_string_and_unregisters()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        var result = f.Manager.SetKey("audio/mute", null);

        Assert.True(result.Success);
        Assert.Equal("", f.State.State.HotkeyKeys["audio/mute"]);
        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/mute").Status);
        Assert.Empty(f.Backend.Registered);
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public void Explicit_none_survives_rebuild_and_blocks_default()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.SetKey("audio/mute", null);

        f.Catalog.RaiseChanged();

        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/mute").Status);
    }

    [Fact]
    public void ResetKey_restores_and_persists_default()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.SetKey("audio/mute", 'N');

        var result = f.Manager.ResetKey("audio/mute");

        Assert.True(result.Success);
        Assert.Equal("M", f.State.State.HotkeyKeys["audio/mute"]); // типове видано знову й записано
        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
        Assert.Equal([CtrlAltM], f.Backend.Registered.Values);
    }

    [Fact]
    public void ResetKey_without_default_reports_failure()
    {
        using var f = Create(Action("audio", "mute"));

        var result = f.Manager.ResetKey("audio/mute");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void ResetKey_taken_by_system_reports_failure()
    {
        using var f = new Fixture();
        f.Backend.Taken.Add(CtrlAltM);
        f.Catalog.Add(Action("audio", "mute", CtrlAltM));
        f.Start();

        var result = f.Manager.ResetKey("audio/mute");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void ResetKey_default_used_by_other_action_reports_failure()
    {
        using var f = Create(
            Action("audio", "mute", CtrlAltM, "Звук"),
            Action("video", "cam", CtrlAltM, "Відео"));
        f.Manager.SetKey("video/cam", 'N');
        f.Manager.SetKey("audio/mute", 'M');

        var result = f.Manager.ResetKey("video/cam");

        Assert.False(result.Success);
    }

    [Fact]
    public void Disabled_unregisters_everything_and_reports_disabled()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM), Action("audio", "other"));

        f.Manager.Enabled = false;

        Assert.False(f.State.State.HotkeysEnabled);
        Assert.Empty(f.Backend.Registered);
        Assert.Equal(HotkeyBindingStatus.Disabled, f.Binding("audio/mute").Status);
        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.Equal(HotkeyBindingStatus.Unassigned, f.Binding("audio/other").Status);
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public void Re_enabling_registers_again()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.Enabled = false;

        f.Manager.Enabled = true;

        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
        Assert.Single(f.Backend.Registered);
    }

    [Fact]
    public void SetKey_while_disabled_skips_system_probe()
    {
        using var f = new Fixture();
        f.State.State.HotkeysEnabled = false;
        f.Backend.Taken.Add(CtrlAltN);
        f.Catalog.Add(Action("audio", "mute"));
        f.Start();

        Assert.True(f.Manager.SetKey("audio/mute", 'N').Success);
        Assert.Empty(f.Backend.RegisterCalls);
    }

    [Fact]
    public async Task Pressed_executes_action_with_hotkey_trigger()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        var id = f.Backend.Registered.Single().Key;

        f.Backend.Press(id);
        await Task.Yield();

        var call = Assert.Single(f.Catalog.Executed);
        Assert.Equal(("audio/mute", ActionTrigger.Hotkey), call);
    }

    [Fact]
    public void Pressed_with_unknown_id_does_nothing()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        f.Backend.Press(9999);

        Assert.Empty(f.Catalog.Executed);
    }

    [Fact]
    public void Pressed_swallows_exceptions_from_catalog()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Catalog.ThrowOnExecute = true;

        f.Backend.Press(f.Backend.Registered.Single().Key);

        Assert.NotEmpty(f.Log.Errors);
    }

    [Fact]
    public void Catalog_change_triggers_rebuild_and_changed_event()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        var raised = 0;
        f.Manager.Changed += (_, _) => raised++;

        f.Catalog.Add(Action("video", "cam", CtrlAltN));
        f.Catalog.RaiseChanged();

        Assert.Equal(2, f.Manager.Bindings.Count);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("video/cam").Status);
        Assert.Equal(2, f.Backend.Registered.Count);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Removed_module_actions_are_unregistered_on_change()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM), Action("video", "cam", CtrlAltN));

        f.Catalog.Remove("video/cam");
        f.Catalog.RaiseChanged();

        Assert.Equal([CtrlAltM], f.Backend.Registered.Values);
    }

    [Fact]
    public void Dispose_unregisters_all_and_unsubscribes()
    {
        var f = Create(Action("audio", "mute", CtrlAltM));
        var calls = f.Backend.RegisterCalls.Count;

        f.Manager.Dispose();
        f.Catalog.RaiseChanged();
        f.Backend.Press(1);

        Assert.Empty(f.Backend.Registered);
        Assert.Equal(calls, f.Backend.RegisterCalls.Count);
        Assert.Empty(f.Catalog.Executed);
    }
}
