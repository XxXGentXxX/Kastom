using Kastom.Modules.Audio.Actions;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Shell;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Modules.Audio.Tests.Module;

public sealed class AudioActionsHintTests
{
    [Fact]
    public async Task HintOnlyForHotkeyAndOnlyWhenOsdIsOn()
    {
        var f = await ActionsFixture.CreateAsync();

        await f.Actions.ExecuteAsync(ActionIds.ToggleMic, ActionTrigger.Ui);
        await f.Actions.ExecuteAsync(ActionIds.ToggleMic, ActionTrigger.Widget);
        await f.Actions.ExecuteAsync(ActionIds.ToggleMic, ActionTrigger.RadialMenu);
        Assert.Empty(f.Osd.Texts);

        f.Settings.Set(SettingKeys.Osd, false);
        await f.Press(ActionIds.ToggleMic);
        Assert.Empty(f.Osd.Texts);

        f.Settings.Set(SettingKeys.Osd, true);
        await f.Press(ActionIds.ToggleMic);
        Assert.Single(f.Osd.Texts);
    }

    [Fact]
    public async Task ResetAppOutputsAlwaysShowsHint()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Settings.Set(SettingKeys.Osd, false);

        await f.Actions.ExecuteAsync(ActionIds.ResetAppOutputs, ActionTrigger.Ui);

        Assert.Contains("ResetAppOutputs", f.Backend.Commands);
        Assert.Equal(Strings.OsdAppOutputsReset, Assert.Single(f.Osd.Texts));
    }

    [Fact]
    public async Task UnavailableBackendOnlySaysSo()
    {
        var f = await ActionsFixture.CreateAsync(start: false);

        await f.Press(ActionIds.ToggleMic);
        await f.Press(ActionIds.VolumeUp);
        await f.Press(ActionIds.ResetAppOutputs);

        Assert.Equal(Enumerable.Repeat(Strings.OsdAudioUnavailable, 3), f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
        Assert.Empty(f.Shell.Errors);
    }

    [Fact]
    public async Task OpenMixerAndTaskbarSettingsNeedNoBackend()
    {
        var f = await ActionsFixture.CreateAsync(start: false);

        await f.Actions.ExecuteAsync(ActionIds.OpenMixer, ActionTrigger.Hotkey);
        await f.Actions.ExecuteAsync(ActionIds.OpenTaskbarSettings, ActionTrigger.Ui);

        Assert.Equal(1, f.MixerOpened);
        Assert.Equal(nameof(IAudioShell.OpenTaskbarSettings), Assert.Single(f.Shell.Calls));
        Assert.Empty(f.Osd.Texts);
    }

    [Theory]
    [InlineData(ActionIds.NextMic)]
    [InlineData(ActionIds.NextOutput)]
    [InlineData(ActionIds.PrevOutput)]
    public async Task FailedDefaultSwitchIsReportedNotThrown(string actionId)
    {
        var f = await ActionsFixture.CreateAsync();
        var failure = new AudioCommandException("відмова");
        f.Backend.FailNext = failure;

        await f.Press(actionId);

        var error = Assert.Single(f.Shell.Errors);
        Assert.Equal(Strings.OsdDefaultSwitchFailed, error.Message);
        Assert.Same(failure, error.Exception);
        Assert.Empty(f.Osd.Texts);
    }

    [Fact]
    public async Task FailedCommandsUseMatchingMessages()
    {
        var f = await ActionsFixture.CreateAsync();

        f.Backend.FailNext = new AudioCommandException("a");
        await f.Press(ActionIds.ToggleMic);
        f.Backend.FailNext = new AudioCommandException("b");
        await f.Press(ActionIds.ResetAppOutputs);
        await f.Press("немає-такої-дії");

        Assert.Equal(
            new[] { Strings.OsdCommandFailed, Strings.OsdAppRoutingFailed, Strings.OsdCommandFailed },
            f.Shell.Errors.Select(e => e.Message));
    }
}

public sealed class AudioActionsMicTests
{
    [Fact]
    public async Task ToggleMicFlipsDefaultMicBothWays()
    {
        var f = await ActionsFixture.CreateAsync();
        var mic = f.Snapshot.DefaultInput!;

        await f.Press(ActionIds.ToggleMic);
        Assert.True(f.Snapshot.FindDevice(mic.Id)!.IsMuted);
        Assert.Equal(Strings.OsdMicMuted, f.LastOsd);

        await f.Press(ActionIds.ToggleMic);
        Assert.False(f.Snapshot.FindDevice(mic.Id)!.IsMuted);
        Assert.Equal(Strings.OsdMicUnmuted, f.LastOsd);
    }

    [Fact]
    public async Task ToggleMicWithoutMicsSaysSo()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Backend.Replace(new AudioSnapshot(f.Snapshot.Outputs.ToList()));

        await f.Press(ActionIds.ToggleMic);
        await f.Press(ActionIds.ToggleAllMics);
        await f.Press(ActionIds.NextMic);

        Assert.Equal(Enumerable.Repeat(Strings.OsdNoMics, 3), f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
    }

    [Fact]
    public async Task ToggleAllMicsMutesAllWhenAnyIsOnThenUnmutesAll()
    {
        var f = await ActionsFixture.CreateAsync();
        Assert.Contains(f.Snapshot.Inputs, m => !m.IsMuted);
        Assert.Contains(f.Snapshot.Inputs, m => m.IsMuted);

        await f.Press(ActionIds.ToggleAllMics);
        Assert.All(f.Snapshot.Inputs, m => Assert.True(m.IsMuted));
        Assert.Equal(Strings.OsdAllMicsMuted, f.LastOsd);

        await f.Press(ActionIds.ToggleAllMics);
        Assert.All(f.Snapshot.Inputs, m => Assert.False(m.IsMuted));
        Assert.Equal(Strings.OsdAllMicsUnmuted, f.LastOsd);
    }

    [Fact]
    public async Task NextMicGoesRoundWithAllRoles()
    {
        var f = await ActionsFixture.CreateAsync();
        var first = f.Snapshot.DefaultInput!;
        var second = f.Snapshot.Inputs.Last();

        await f.Press(ActionIds.NextMic);
        Assert.Equal(second.Id, f.Snapshot.DefaultInput!.Id);
        Assert.Equal(Strings.Format(Strings.OsdMicSwitchedFormat, second.Name), f.LastOsd);
        Assert.Contains($"SetDefaultDevice {second.Id} {DefaultRoles.All}", f.Backend.Commands);

        await f.Press(ActionIds.NextMic);
        Assert.Equal(first.Id, f.Snapshot.DefaultInput!.Id);
    }

    [Fact]
    public async Task NextMicSetsOnlyNormalRolesWhenCommunicationsAreSeparate()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Settings.Set(SettingKeys.SeparateCommunications, true);
        var second = f.Snapshot.Inputs.Last();

        await f.Press(ActionIds.NextMic);

        Assert.Contains($"SetDefaultDevice {second.Id} {DefaultRoles.Normal}", f.Backend.Commands);
        Assert.False(f.Snapshot.FindDevice(second.Id)!.IsDefaultCommunications);
    }

    [Fact]
    public async Task MicSlotFindsMicByExactNameThenByPartOfName()
    {
        var f = await ActionsFixture.CreateAsync();
        var webcam = f.Device("Мікрофон (C922");
        var headset = f.Device("Мікрофон (HyperX");

        f.Options.SetMicSlot(1, "c922");
        await f.Press(ActionIds.MicSlot(1));
        Assert.False(f.Snapshot.FindDevice(webcam.Id)!.IsMuted);
        Assert.Equal(Strings.Format(Strings.OsdNamedUnmutedFormat, webcam.Name), f.LastOsd);

        f.Options.SetMicSlot(2, "мікрофон (hyperx cloud ii)");
        await f.Press(ActionIds.MicSlot(2));
        Assert.True(f.Snapshot.FindDevice(headset.Id)!.IsMuted);
        Assert.Equal(Strings.Format(Strings.OsdNamedMutedFormat, headset.Name), f.LastOsd);

        // Частина назви, що збігається з обома, дає перший у знімку.
        f.Options.SetMicSlot(1, "Мікрофон");
        await f.Press(ActionIds.MicSlot(1));
        Assert.False(f.Snapshot.FindDevice(headset.Id)!.IsMuted);
    }

    [Fact]
    public async Task EmptyAndUnknownMicSlotsAreExplained()
    {
        var f = await ActionsFixture.CreateAsync();

        await f.Press(ActionIds.MicSlot(1));
        f.Options.SetMicSlot(2, "студійний");
        await f.Press(ActionIds.MicSlot(2));

        Assert.Equal(
            new[] { Strings.Format(Strings.OsdMicSlotEmptyFormat, 1), Strings.Format(Strings.OsdMicNotFoundFormat, "студійний") },
            f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
    }
}

public sealed class AudioActionsOutputTests
{
    [Fact]
    public async Task ToggleOutputFlipsDefaultOutputBothWays()
    {
        var f = await ActionsFixture.CreateAsync();
        var output = f.Snapshot.DefaultOutput!;

        await f.Press(ActionIds.ToggleOutput);
        Assert.True(f.Snapshot.FindDevice(output.Id)!.IsMuted);
        Assert.Equal(Strings.OsdOutputMuted, f.LastOsd);

        await f.Press(ActionIds.ToggleOutput);
        Assert.False(f.Snapshot.FindDevice(output.Id)!.IsMuted);
        Assert.Equal(Strings.OsdOutputUnmuted, f.LastOsd);
    }

    [Fact]
    public async Task NextAndPrevOutputGoRoundInSnapshotOrder()
    {
        var f = await ActionsFixture.CreateAsync();
        var outputs = f.Snapshot.Outputs.ToList();

        await f.Press(ActionIds.NextOutput);
        Assert.Equal(outputs[1].Id, f.Snapshot.DefaultOutput!.Id);
        Assert.Equal(Strings.Format(Strings.OsdOutputSwitchedFormat, outputs[1].Name), f.LastOsd);
        await f.Press(ActionIds.NextOutput);
        await f.Press(ActionIds.NextOutput);
        Assert.Equal(outputs[0].Id, f.Snapshot.DefaultOutput!.Id);

        await f.Press(ActionIds.PrevOutput);
        Assert.Equal(outputs[2].Id, f.Snapshot.DefaultOutput!.Id);
        await f.Press(ActionIds.PrevOutput);
        Assert.Equal(outputs[1].Id, f.Snapshot.DefaultOutput!.Id);
    }

    [Fact]
    public async Task OutputSwitchSetsAllRolesByDefaultAndNormalWhenSeparate()
    {
        var f = await ActionsFixture.CreateAsync();
        var outputs = f.Snapshot.Outputs.ToList();

        await f.Press(ActionIds.NextOutput);
        Assert.Contains($"SetDefaultDevice {outputs[1].Id} {DefaultRoles.All}", f.Backend.Commands);
        Assert.True(f.Snapshot.FindDevice(outputs[1].Id)!.IsDefaultCommunications);

        f.Settings.Set(SettingKeys.SeparateCommunications, true);
        await f.Press(ActionIds.NextOutput);
        Assert.Contains($"SetDefaultDevice {outputs[2].Id} {DefaultRoles.Normal}", f.Backend.Commands);
        Assert.True(f.Snapshot.FindDevice(outputs[2].Id)!.IsDefault);
        Assert.True(f.Snapshot.FindDevice(outputs[1].Id)!.IsDefaultCommunications);
    }

    [Fact]
    public async Task SingleOutputChangesNothingButNamesIt()
    {
        var f = await ActionsFixture.CreateAsync();
        var only = f.Snapshot.DefaultOutput!;
        f.Backend.Replace(new AudioSnapshot([only]));

        await f.Press(ActionIds.NextOutput);
        await f.Press(ActionIds.PrevOutput);

        Assert.Empty(f.Backend.Commands);
        Assert.Equal(Enumerable.Repeat(Strings.Format(Strings.OsdOutputSwitchedFormat, only.Name), 2), f.Osd.Texts);
    }

    [Fact]
    public async Task WithoutDefaultOutputSwitchStartsFromTheFirst()
    {
        var f = await ActionsFixture.CreateAsync();
        var outputs = f.Snapshot.Outputs.Select(d => d with { IsDefault = false }).ToList();
        f.Backend.Replace(new AudioSnapshot(outputs));

        await f.Press(ActionIds.PrevOutput);

        Assert.Contains($"SetDefaultDevice {outputs[0].Id} {DefaultRoles.All}", f.Backend.Commands);
    }

    [Fact]
    public async Task NoOutputsSaysSo()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Backend.Replace(new AudioSnapshot(f.Snapshot.Inputs.ToList()));

        await f.Press(ActionIds.NextOutput);
        await f.Press(ActionIds.ToggleOutput);
        await f.Press(ActionIds.VolumeUp);

        Assert.Equal(Enumerable.Repeat(Strings.OsdNoOutputs, 3), f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
    }

    [Fact]
    public async Task VolumeKeysStepToNextMultiple()
    {
        var f = await ActionsFixture.CreateAsync();
        var output = f.Snapshot.DefaultOutput!;

        await f.Press(ActionIds.VolumeUp);
        Assert.Equal(46, VolumeMath.ToPercent(f.Snapshot.FindDevice(output.Id)!.Volume));
        Assert.Equal(Strings.Format(Strings.OsdVolumeFormat, 46), f.LastOsd);

        await f.Press(ActionIds.VolumeDown);
        await f.Press(ActionIds.VolumeDown);
        Assert.Equal(42, VolumeMath.ToPercent(f.Snapshot.FindDevice(output.Id)!.Volume));
        Assert.Equal(Strings.Format(Strings.OsdVolumeFormat, 42), f.LastOsd);
    }

    [Fact]
    public async Task VolumeUpUnmutesButVolumeDownDoesNot()
    {
        var f = await ActionsFixture.CreateAsync();
        var muted = f.Snapshot.Outputs.First(d => d.IsMuted);
        await f.Backend.SetDefaultDeviceAsync(muted.Id, DefaultRoles.All);

        await f.Press(ActionIds.VolumeDown);
        Assert.True(f.Snapshot.FindDevice(muted.Id)!.IsMuted);
        Assert.Equal(98, VolumeMath.ToPercent(f.Snapshot.FindDevice(muted.Id)!.Volume));

        await f.Press(ActionIds.VolumeUp);
        Assert.False(f.Snapshot.FindDevice(muted.Id)!.IsMuted);
        Assert.Equal(100, VolumeMath.ToPercent(f.Snapshot.FindDevice(muted.Id)!.Volume));
    }

    [Fact]
    public async Task VolumeStepFollowsTheSetting()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Settings.Set(SettingKeys.VolumeStep, 10);

        await f.Press(ActionIds.VolumeUp);

        Assert.Equal(50, VolumeMath.ToPercent(f.Snapshot.DefaultOutput!.Volume));
    }
}

public sealed class AudioActionsAppTests
{
    private static ForegroundProcess Process(int id, string name) => new(id, name);

    [Fact]
    public async Task ActiveAppIsFoundByProcessId()
    {
        var f = await ActionsFixture.CreateAsync();
        var chrome = f.App("chrome");
        f.Foreground.Current = Process(chrome.ProcessIds[1], "якийсь-інший");

        await f.Press(ActionIds.ToggleActiveApp);

        Assert.True(f.App("chrome").IsMuted);
        Assert.Equal(Strings.Format(Strings.OsdAppMutedFormat, chrome.Name), f.LastOsd);

        await f.Press(ActionIds.ToggleActiveApp);
        Assert.False(f.App("chrome").IsMuted);
        Assert.Equal(Strings.Format(Strings.OsdAppUnmutedFormat, chrome.Name), f.LastOsd);
    }

    [Fact]
    public async Task ActiveAppFallsBackToProcessNameIgnoringCase()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Foreground.Current = Process(1, "Discord");

        await f.Press(ActionIds.ToggleActiveApp);

        Assert.True(f.App("discord").IsMuted);
    }

    [Fact]
    public async Task ActiveAppNotPlayingOrUnknownSaysSo()
    {
        var f = await ActionsFixture.CreateAsync();

        f.Foreground.Current = null;
        await f.Press(ActionIds.ToggleActiveApp);
        f.Foreground.Current = Process(1, "notepad");
        await f.Press(ActionIds.ActiveAppVolumeUp);
        await f.Press(ActionIds.ActiveAppVolumeDown);

        Assert.Equal(Enumerable.Repeat(Strings.OsdNoActiveApp, 3), f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
    }

    [Fact]
    public async Task ActiveAppOnTwoDevicesIsChangedEverywhere()
    {
        var f = await ActionsFixture.CreateAsync();
        var chrome = f.App("chrome");
        var speakers = f.Snapshot.Outputs.First(d => d.Id != chrome.DeviceId && !d.IsMuted);
        var onSpeakers = chrome with { DeviceId = speakers.Id, IsMuted = true, Volume = 0.3f };
        f.Backend.Replace(f.Snapshot with
        {
            Devices = f.Snapshot.Devices.Select(d => d.Id == speakers.Id ? d with { Apps = [.. d.Apps, onSpeakers] } : d).ToArray(),
        });
        f.Foreground.Current = Process(chrome.ProcessIds[0], "chrome");

        // Один сеанс уже вимкнено, інший ні: вимикаються всі.
        await f.Press(ActionIds.ToggleActiveApp);
        Assert.All(f.Snapshot.AllApps.Where(a => a.ProcessName == "chrome"), a => Assert.True(a.IsMuted));
        await f.Press(ActionIds.ToggleActiveApp);
        Assert.All(f.Snapshot.AllApps.Where(a => a.ProcessName == "chrome"), a => Assert.False(a.IsMuted));

        // Гучність рахується від першого збігу, нове значення однакове для всіх.
        await f.Press(ActionIds.ActiveAppVolumeUp);
        var volumes = f.Snapshot.AllApps.Where(a => a.ProcessName == "chrome").Select(a => VolumeMath.ToPercent(a.Volume)).ToList();
        Assert.Equal(new[] { 82, 82 }, volumes);
        Assert.Equal(Strings.Format(Strings.OsdAppVolumeFormat, chrome.Name, 82), f.LastOsd);
    }

    [Fact]
    public async Task ActiveAppVolumeUpUnmutesAndDownDoesNot()
    {
        var f = await ActionsFixture.CreateAsync();
        var cs2 = f.App("cs2");
        Assert.True(cs2.IsMuted);
        f.Foreground.Current = Process(cs2.ProcessIds[0], "cs2");

        await f.Press(ActionIds.ActiveAppVolumeDown);
        Assert.True(f.App("cs2").IsMuted);
        Assert.Equal(58, VolumeMath.ToPercent(f.App("cs2").Volume));

        await f.Press(ActionIds.ActiveAppVolumeUp);
        Assert.False(f.App("cs2").IsMuted);
        Assert.Equal(60, VolumeMath.ToPercent(f.App("cs2").Volume));
    }

    [Fact]
    public async Task AppSlotToggleAndVolumeWorkByProcessName()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Options.SetAppSlot(1, "Spotify.exe");

        await f.Press(ActionIds.AppSlotToggle(1));
        Assert.True(f.App("spotify").IsMuted);
        Assert.Equal(Strings.Format(Strings.OsdAppMutedFormat, "Spotify"), f.LastOsd);

        await f.Press(ActionIds.AppSlotUp(1));
        Assert.False(f.App("spotify").IsMuted);
        Assert.Equal(72, VolumeMath.ToPercent(f.App("spotify").Volume));

        await f.Press(ActionIds.AppSlotDown(1));
        Assert.Equal(70, VolumeMath.ToPercent(f.App("spotify").Volume));
        Assert.Equal(Strings.Format(Strings.OsdAppVolumeFormat, "Spotify", 70), f.LastOsd);
    }

    [Fact]
    public async Task EmptyAndSilentAppSlotsAreExplained()
    {
        var f = await ActionsFixture.CreateAsync();

        await f.Press(ActionIds.AppSlotToggle(2));
        f.Options.SetAppSlot(3, "obs64");
        await f.Press(ActionIds.AppSlotUp(3));
        await f.Press(ActionIds.AppSlotDown(3));

        Assert.Equal(
            new[]
            {
                Strings.Format(Strings.OsdAppSlotEmptyFormat, 2),
                Strings.Format(Strings.OsdAppNotPlayingFormat, "obs64"),
                Strings.Format(Strings.OsdAppNotPlayingFormat, "obs64"),
            },
            f.Osd.Texts);
        Assert.Empty(f.Backend.Commands);
    }

    [Fact]
    public async Task ErrorWhileChangingAppIsReported()
    {
        var f = await ActionsFixture.CreateAsync();
        f.Options.SetAppSlot(1, "discord");
        f.Backend.FailNext = new AudioCommandException("відмова");

        await f.Press(ActionIds.AppSlotToggle(1));

        Assert.Equal(Strings.OsdCommandFailed, Assert.Single(f.Shell.Errors).Message);
        Assert.False(f.App("discord").IsMuted);
    }
}

public sealed class AudioActionsRegistrationTests
{
    private static async Task<(TestActionRegistry Registry, ActionsFixture Fixture)> RegisterAsync()
    {
        var f = await ActionsFixture.CreateAsync();
        var registry = new TestActionRegistry();
        f.Actions.Register(registry);
        return (registry, f);
    }

    [Fact]
    public async Task RegistersEveryActionIdOnce()
    {
        var (registry, _) = await RegisterAsync();

        var expected = new List<string>
        {
            ActionIds.ToggleMic, ActionIds.ToggleAllMics, ActionIds.NextMic, ActionIds.ToggleOutput, ActionIds.NextOutput,
            ActionIds.PrevOutput, ActionIds.VolumeUp, ActionIds.VolumeDown, ActionIds.ToggleActiveApp,
            ActionIds.ActiveAppVolumeUp, ActionIds.ActiveAppVolumeDown, ActionIds.OpenMixer, ActionIds.ResetAppOutputs,
            ActionIds.OpenTaskbarSettings,
        };
        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++) expected.Add(ActionIds.MicSlot(slot));
        for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
            expected.AddRange([ActionIds.AppSlotToggle(slot), ActionIds.AppSlotUp(slot), ActionIds.AppSlotDown(slot)]);

        Assert.Equal(expected.Order(), registry.Actions.Select(a => a.Id).Order());
        Assert.All(registry.Actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Title)));
        Assert.Equal(registry.Actions.Count, registry.Actions.Select(a => a.Title).Distinct().Count());
    }

    [Fact]
    public async Task DefaultHotkeysShareTheCtrlAltAddress()
    {
        var (registry, _) = await RegisterAsync();

        var hotkeys = registry.Actions.Where(a => a.DefaultHotkey is not null).ToDictionary(a => a.Id, a => a.DefaultHotkey!.Value);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [ActionIds.ToggleMic] = "Ctrl+Alt+M",
                [ActionIds.NextOutput] = "Ctrl+Alt+O",
                [ActionIds.VolumeUp] = "Ctrl+Alt+Up",
                [ActionIds.VolumeDown] = "Ctrl+Alt+Down",
                [ActionIds.OpenMixer] = "Ctrl+Alt+X",
            },
            hotkeys.ToDictionary(p => p.Key, p => p.Value.ToString()));
        Assert.All(hotkeys.Values, h => Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, h.Modifiers));
        Assert.All(hotkeys.Values, h => Assert.True(h.IsValid));
        Assert.Equal(0x26, hotkeys[ActionIds.VolumeUp].VirtualKey);
        Assert.Equal(0x28, hotkeys[ActionIds.VolumeDown].VirtualKey);
        Assert.DoesNotContain(hotkeys.Values, h => h.VirtualKey == 'U');
    }

    [Fact]
    public async Task ExecuteRunsTheActionWithItsTrigger()
    {
        var (registry, f) = await RegisterAsync();
        var toggleMic = registry.Actions.Single(a => a.Id == ActionIds.ToggleMic);

        await toggleMic.Execute(new ActionContext { Trigger = ActionTrigger.Hotkey });

        Assert.True(f.Snapshot.DefaultInput!.IsMuted);
        Assert.Equal(Strings.OsdMicMuted, f.LastOsd);

        await registry.Actions.Single(a => a.Id == ActionIds.OpenMixer).Execute(new ActionContext { Trigger = ActionTrigger.Ui });
        Assert.Equal(1, f.MixerOpened);
    }
}
