using Kastom.Core.Abstractions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Tests.Hotkeys;

public partial class HotkeyManagerTests
{
    [Fact]
    public void Suspend_unregisters_everything_and_dispose_restores()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        var token = f.Manager.Suspend();
        Assert.Empty(f.Backend.Registered);

        token.Dispose();
        Assert.Equal([CtrlAltM], f.Backend.Registered.Values);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
    }

    [Fact]
    public void Nested_suspend_restores_only_after_the_last_dispose_and_double_dispose_is_harmless()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        var first = f.Manager.Suspend();
        var second = f.Manager.Suspend();
        first.Dispose();
        first.Dispose();
        Assert.Empty(f.Backend.Registered);

        second.Dispose();
        Assert.Single(f.Backend.Registered);

        // Лічильник не пішов у мінус: новий Suspend знову працює.
        using (f.Manager.Suspend()) Assert.Empty(f.Backend.Registered);
        Assert.Single(f.Backend.Registered);
    }

    [Fact]
    public void Assign_while_suspended_takes_effect_on_resume()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        var token = f.Manager.Suspend();

        Assert.True(f.Manager.Assign("audio/mute", CtrlAltN).Success);
        Assert.Empty(f.Backend.Registered);

        token.Dispose();
        Assert.Equal([CtrlAltN], f.Backend.Registered.Values);
    }

    [Fact]
    public void Suspended_assign_of_gesture_used_by_other_action_reports_conflict()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM, "Звук"), Action("video", "cam"));
        using var token = f.Manager.Suspend();

        var result = f.Manager.Assign("video/cam", CtrlAltM);

        Assert.False(result.Success);
        Assert.Equal("audio/mute", result.ConflictingActionId);
    }

    [Fact]
    public void Granted_default_is_persisted_into_state()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));

        Assert.Equal("Ctrl+Alt+M", f.State.State.Hotkeys["audio/mute"]);
        Assert.Equal(1, f.State.SaveCount);
    }

    [Fact]
    public void Later_module_with_same_default_does_not_steal_it()
    {
        using var f = Create(Action("zeta", "mute", CtrlAltM, "Яблуко"));
        f.Catalog.Add(Action("alpha", "mute", CtrlAltM, "Барвінок")); // за назвою стоїть раніше
        f.Catalog.RaiseChanged();

        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("zeta/mute").Status);
        Assert.Equal(CtrlAltM, f.Binding("zeta/mute").Gesture);
        var newcomer = f.Binding("alpha/mute");
        Assert.Null(newcomer.Gesture);
        Assert.Equal(HotkeyBindingStatus.Unassigned, newcomer.Status);
        Assert.DoesNotContain("alpha/mute", f.State.State.Hotkeys.Keys);
    }

    [Fact]
    public void Reset_reports_holder_when_default_is_taken_and_regrants_when_free()
    {
        using var f = Create(Action("zeta", "mute", CtrlAltM, "Яблуко"));
        f.Catalog.Add(Action("alpha", "mute", CtrlAltM, "Барвінок"));
        f.Catalog.RaiseChanged();

        var blocked = f.Manager.ResetToDefault("alpha/mute");
        Assert.False(blocked.Success);
        Assert.Equal("zeta/mute", blocked.ConflictingActionId);
        Assert.Contains("Яблуко", blocked.Error);

        f.Manager.Assign("zeta/mute", null);
        var free = f.Manager.ResetToDefault("alpha/mute");

        Assert.True(free.Success);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("alpha/mute").Status);
        Assert.Equal("Ctrl+Alt+M", f.State.State.Hotkeys["alpha/mute"]);
    }

    [Fact]
    public void Ids_are_stable_and_within_range_after_many_rebuilds()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM), Action("audio", "other", CtrlAltN));
        var before = f.Backend.Registered.ToDictionary(p => p.Value, p => p.Key);

        for (var i = 0; i < 50; i++)
        {
            f.Catalog.RaiseChanged();
            f.Manager.Enabled = i % 2 == 1;
            f.Manager.Assign("audio/mute", i % 2 == 0 ? CtrlAltK : CtrlAltM);
            using (f.Manager.Suspend())
            {
            }
        }

        f.Manager.Enabled = true;
        f.Manager.Assign("audio/mute", CtrlAltM);

        var after = f.Backend.Registered.ToDictionary(p => p.Value, p => p.Key);
        Assert.Equal(before[CtrlAltM], after[CtrlAltM]);
        Assert.Equal(before[CtrlAltN], after[CtrlAltN]);
        Assert.All(f.Backend.RegisterCalls, call => Assert.InRange(call.Id, 0, 0xBFFF));
        var actionIds = f.Backend.RegisterCalls.Where(c => c.Id != 0xBFFF).Select(c => c.Id).Distinct().ToList();
        Assert.True(actionIds.Count <= 3, "по одному id на дію (+ id під прив'язку K)");
    }

    [Fact]
    public void Assign_save_failure_reverts_state_and_registrations()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.State.ThrowOnSave = true;

        var result = f.Manager.Assign("audio/mute", CtrlAltN);

        Assert.False(result.Success);
        Assert.Contains("disk full", result.Error);
        Assert.Equal("Ctrl+Alt+M", f.State.State.Hotkeys["audio/mute"]);
        Assert.Equal(CtrlAltM, f.Binding("audio/mute").Gesture);
        Assert.Equal([CtrlAltM], f.Backend.Registered.Values);
        Assert.NotEmpty(f.Log.Errors);
    }

    [Fact]
    public void Reset_save_failure_reverts_state_and_registrations()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.Manager.Assign("audio/mute", CtrlAltN);
        f.State.ThrowOnSave = true;

        var result = f.Manager.ResetToDefault("audio/mute");

        Assert.False(result.Success);
        Assert.Equal("Ctrl+Alt+N", f.State.State.Hotkeys["audio/mute"]);
        Assert.Equal([CtrlAltN], f.Backend.Registered.Values);
    }

    [Fact]
    public void Enabled_save_failure_reverts_flag()
    {
        using var f = Create(Action("audio", "mute", CtrlAltM));
        f.State.ThrowOnSave = true;

        f.Manager.Enabled = false;

        Assert.True(f.Manager.Enabled);
        Assert.Equal(HotkeyBindingStatus.Active, f.Binding("audio/mute").Status);
        Assert.Single(f.Backend.Registered);
    }
}
