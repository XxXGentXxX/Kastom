using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Shell;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.Actions;

/// <summary>
/// Дії для гарячих клавіш, кругового меню й кнопок (З13). Підказка на екрані (З15) з'являється лише після клавіші
/// й лише якщо її не вимкнено в налаштуваннях.
/// </summary>
internal sealed class AudioActions(MixerHost host, INotificationService osd, IForegroundApp foreground, Action openMixer)
{
    // Усі типові поєднання мають однакові модифікатори Ctrl+Alt: так «адресу модуля» легко запам'ятати.
    // Літеру U не беремо: AltGr+Г дає «ґ». Стрілки теж: Ctrl+Alt+Up/Down у VS Code — мультикурсор.
    private const HotkeyModifiers Address = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    public void Register(IActionRegistry registry)
    {
        Add(registry, ActionIds.ToggleMic, Strings.ActionToggleMic, hotkey: 'M');
        Add(registry, ActionIds.ToggleAllMics, Strings.ActionToggleAllMics);
        Add(registry, ActionIds.NextMic, Strings.ActionNextMic);
        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++)
        {
            Add(
                registry,
                ActionIds.MicSlot(slot),
                Strings.Format(Strings.ActionMicSlotFormat, slot),
                Strings.Format(Strings.ActionMicSlotDescriptionFormat, slot));
        }

        Add(registry, ActionIds.ToggleOutput, Strings.ActionToggleOutput);
        Add(registry, ActionIds.NextOutput, Strings.ActionNextOutput, hotkey: 'O');
        Add(registry, ActionIds.PrevOutput, Strings.ActionPrevOutput);
        Add(registry, ActionIds.VolumeUp, Strings.ActionVolumeUp, hotkey: 0x21);
        Add(registry, ActionIds.VolumeDown, Strings.ActionVolumeDown, hotkey: 0x22);
        Add(registry, ActionIds.ToggleActiveApp, Strings.ActionToggleActiveApp, Strings.ActionActiveAppDescription);
        Add(registry, ActionIds.ActiveAppVolumeUp, Strings.ActionActiveAppVolumeUp, Strings.ActionActiveAppDescription);
        Add(registry, ActionIds.ActiveAppVolumeDown, Strings.ActionActiveAppVolumeDown, Strings.ActionActiveAppDescription);
        for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
        {
            var description = Strings.Format(Strings.ActionAppSlotDescriptionFormat, slot);
            Add(registry, ActionIds.AppSlotToggle(slot), Strings.Format(Strings.ActionAppSlotToggleFormat, slot), description);
            Add(registry, ActionIds.AppSlotUp(slot), Strings.Format(Strings.ActionAppSlotUpFormat, slot), description);
            Add(registry, ActionIds.AppSlotDown(slot), Strings.Format(Strings.ActionAppSlotDownFormat, slot), description);
        }

        Add(registry, ActionIds.OpenMixer, Strings.ActionOpenMixer, hotkey: 'X');
        Add(registry, ActionIds.ResetAppOutputs, Strings.ActionResetAppOutputs);
        Add(registry, ActionIds.OpenTaskbarSettings, Strings.ActionOpenTaskbarSettings);
    }

    /// <summary>Уся логіка дій. Винятки назовні не виходять: помилка команди стає підказкою й записом у лог.</summary>
    internal async Task ExecuteAsync(string actionId, ActionTrigger trigger)
    {
        try
        {
            switch (actionId)
            {
                case ActionIds.OpenMixer:
                    openMixer();
                    return;
                case ActionIds.OpenTaskbarSettings:
                    host.Shell.OpenTaskbarSettings();
                    return;
            }

            var snapshot = host.Backend.Current;
            if (!snapshot.IsAvailable)
            {
                Hint(Strings.OsdAudioUnavailable, trigger);
                return;
            }

            var text = await RunAsync(actionId, snapshot);

            // Скидання виводу програм зазвичай натискають кнопкою в налаштуваннях: без підказки відгуку не було б.
            Hint(text, trigger, always: actionId == ActionIds.ResetAppOutputs);
        }
        catch (Exception ex)
        {
            host.Shell.ReportError(FailureMessage(actionId), ex);
        }
    }

    private static string FailureMessage(string actionId) => actionId switch
    {
        ActionIds.NextMic or ActionIds.NextOutput or ActionIds.PrevOutput => Strings.OsdDefaultSwitchFailed,
        ActionIds.ResetAppOutputs => Strings.OsdAppRoutingFailed,
        _ => Strings.OsdCommandFailed,
    };

    private void Add(IActionRegistry registry, string id, string title, string? description = null, int? hotkey = null) =>
        registry.Register(new ModuleAction
        {
            Id = id,
            Title = title,
            Description = description,
            DefaultHotkey = hotkey is { } key ? new HotkeyGesture(Address, key) : null,
            Execute = context => ExecuteAsync(id, context.Trigger),
        });

    private void Hint(string text, ActionTrigger trigger, bool always = false)
    {
        if (always || (trigger == ActionTrigger.Hotkey && host.Options.Osd)) osd.ShowOsd(text);
    }

    private async Task<string> RunAsync(string actionId, AudioSnapshot snapshot)
    {
        switch (actionId)
        {
            case ActionIds.ToggleMic: return await ToggleDefaultMicAsync(snapshot);
            case ActionIds.ToggleAllMics: return await ToggleAllMicsAsync(snapshot);
            case ActionIds.NextMic: return await SwitchDefaultAsync(snapshot, AudioFlow.Capture, +1);
            case ActionIds.ToggleOutput: return await ToggleDefaultOutputAsync(snapshot);
            case ActionIds.NextOutput: return await SwitchDefaultAsync(snapshot, AudioFlow.Render, +1);
            case ActionIds.PrevOutput: return await SwitchDefaultAsync(snapshot, AudioFlow.Render, -1);
            case ActionIds.VolumeUp: return await ChangeOutputVolumeAsync(snapshot, up: true);
            case ActionIds.VolumeDown: return await ChangeOutputVolumeAsync(snapshot, up: false);
            case ActionIds.ToggleActiveApp: return await ToggleAppsAsync(FindActiveApp(snapshot), snapshot, Strings.OsdNoActiveApp);
            case ActionIds.ActiveAppVolumeUp: return await ChangeAppVolumeAsync(FindActiveApp(snapshot), snapshot, up: true, Strings.OsdNoActiveApp);
            case ActionIds.ActiveAppVolumeDown: return await ChangeAppVolumeAsync(FindActiveApp(snapshot), snapshot, up: false, Strings.OsdNoActiveApp);
            case ActionIds.ResetAppOutputs:
                await host.Backend.ResetAppOutputsAsync();
                return Strings.OsdAppOutputsReset;
        }

        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++)
        {
            if (actionId == ActionIds.MicSlot(slot)) return await ToggleMicSlotAsync(snapshot, slot);
        }

        for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
        {
            if (actionId == ActionIds.AppSlotToggle(slot)) return await RunAppSlotAsync(snapshot, slot, up: null);
            if (actionId == ActionIds.AppSlotUp(slot)) return await RunAppSlotAsync(snapshot, slot, up: true);
            if (actionId == ActionIds.AppSlotDown(slot)) return await RunAppSlotAsync(snapshot, slot, up: false);
        }

        throw new ArgumentException($"Невідома дія '{actionId}'.", nameof(actionId));
    }

    private async Task<string> ToggleDefaultMicAsync(AudioSnapshot snapshot)
    {
        if (snapshot.DefaultInput is not { } mic) return Strings.OsdNoMics;

        await host.Backend.SetDeviceMuteAsync(mic.Id, !mic.IsMuted);
        return mic.IsMuted ? Strings.OsdMicUnmuted : Strings.OsdMicMuted;
    }

    private async Task<string> ToggleAllMicsAsync(AudioSnapshot snapshot)
    {
        var mics = snapshot.Inputs.ToList();
        if (mics.Count == 0) return Strings.OsdNoMics;

        var mute = mics.Any(m => !m.IsMuted);
        foreach (var mic in mics) await host.Backend.SetDeviceMuteAsync(mic.Id, mute);
        return mute ? Strings.OsdAllMicsMuted : Strings.OsdAllMicsUnmuted;
    }

    private async Task<string> ToggleMicSlotAsync(AudioSnapshot snapshot, int slot)
    {
        var wanted = host.Options.MicSlot(slot);
        if (wanted.Length == 0) return Strings.Format(Strings.OsdMicSlotEmptyFormat, slot);

        var mics = snapshot.Inputs.ToList();
        var mic = mics.FirstOrDefault(m => m.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                  ?? mics.FirstOrDefault(m => m.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (mic is null) return Strings.Format(Strings.OsdMicNotFoundFormat, wanted);

        await host.Backend.SetDeviceMuteAsync(mic.Id, !mic.IsMuted);
        return Strings.Format(mic.IsMuted ? Strings.OsdNamedUnmutedFormat : Strings.OsdNamedMutedFormat, mic.Name);
    }

    /// <summary>Наступний чи попередній пристрій по колу в порядку знімка; без поточного — перший.</summary>
    private async Task<string> SwitchDefaultAsync(AudioSnapshot snapshot, AudioFlow flow, int direction)
    {
        var devices = snapshot.Devices.Where(d => d.Flow == flow).ToList();
        var inputs = flow == AudioFlow.Capture;
        if (devices.Count == 0) return inputs ? Strings.OsdNoMics : Strings.OsdNoOutputs;

        var current = devices.FindIndex(d => d.IsDefault);
        var target = devices[current < 0 ? 0 : (current + direction + devices.Count) % devices.Count];
        if (devices.Count > 1) await host.Backend.SetDefaultDeviceAsync(target.Id, host.Options.RolesFor(false));
        return Strings.Format(inputs ? Strings.OsdMicSwitchedFormat : Strings.OsdOutputSwitchedFormat, target.Name);
    }

    private async Task<string> ToggleDefaultOutputAsync(AudioSnapshot snapshot)
    {
        if (snapshot.DefaultOutput is not { } output) return Strings.OsdNoOutputs;

        await host.Backend.SetDeviceMuteAsync(output.Id, !output.IsMuted);
        return output.IsMuted ? Strings.OsdOutputUnmuted : Strings.OsdOutputMuted;
    }

    private async Task<string> ChangeOutputVolumeAsync(AudioSnapshot snapshot, bool up)
    {
        if (snapshot.DefaultOutput is not { } output) return Strings.OsdNoOutputs;

        var volume = VolumeMath.Step(output.Volume, host.Options.VolumeStepPercent, up);
        await host.Backend.SetDeviceVolumeAsync(output.Id, volume);
        if (up && output.IsMuted) await host.Backend.SetDeviceMuteAsync(output.Id, false);
        return Strings.Format(Strings.OsdVolumeFormat, VolumeMath.ToPercent(volume));
    }

    private async Task<string> RunAppSlotAsync(AudioSnapshot snapshot, int slot, bool? up)
    {
        var wanted = host.Options.AppSlot(slot);
        if (wanted.Length == 0) return Strings.Format(Strings.OsdAppSlotEmptyFormat, slot);

        var apps = snapshot.AllApps
            .Where(a => string.Equals(a.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var notPlaying = Strings.Format(Strings.OsdAppNotPlayingFormat, wanted);
        return up is { } direction
            ? await ChangeAppVolumeAsync(apps, snapshot, direction, notPlaying)
            : await ToggleAppsAsync(apps, snapshot, notPlaying);
    }

    /// <summary>
    /// Програма на передньому плані: за номером процесу, а якщо звук грає з іншого процесу цієї ж програми
    /// (браузер, Electron) — за назвою процесу.
    /// </summary>
    private List<AudioApp> FindActiveApp(AudioSnapshot snapshot)
    {
        if (foreground.Get() is not { } active) return [];

        var apps = snapshot.AllApps.ToList();
        var byId = apps.Where(a => a.ProcessIds.Contains(active.ProcessId)).ToList();
        return byId.Count > 0
            ? byId
            : apps.Where(a => string.Equals(a.ProcessName, active.ProcessName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Сеанс, від якого рахуємо нове значення: той, що звучить, інакше той, що на пристрої за замовчуванням,
    /// інакше перший. Старий неактивний сеанс тієї ж програми на іншому виході не має задавати гучність.
    /// </summary>
    internal static AudioApp LeadApp(IReadOnlyList<AudioApp> apps, AudioSnapshot snapshot)
    {
        var defaultId = snapshot.DefaultOutput?.Id;
        return apps.FirstOrDefault(a => a.IsActive)
               ?? apps.FirstOrDefault(a => a.DeviceId == defaultId)
               ?? apps[0];
    }

    /// <summary>Усі збіги отримують протилежне до провідного сеансу значення (одна програма може звучати на двох пристроях).</summary>
    private async Task<string> ToggleAppsAsync(List<AudioApp> apps, AudioSnapshot snapshot, string missing)
    {
        if (apps.Count == 0) return missing;

        var lead = LeadApp(apps, snapshot);
        var mute = !lead.IsMuted;
        foreach (var app in apps) await host.Backend.SetAppMuteAsync(app.DeviceId, app.Key, mute);
        return Strings.Format(mute ? Strings.OsdAppMutedFormat : Strings.OsdAppUnmutedFormat, lead.Name);
    }

    private async Task<string> ChangeAppVolumeAsync(List<AudioApp> apps, AudioSnapshot snapshot, bool up, string missing)
    {
        if (apps.Count == 0) return missing;

        var lead = LeadApp(apps, snapshot);
        var volume = VolumeMath.Step(lead.Volume, host.Options.VolumeStepPercent, up);
        foreach (var app in apps)
        {
            await host.Backend.SetAppVolumeAsync(app.DeviceId, app.Key, volume);
            if (up && app.IsMuted) await host.Backend.SetAppMuteAsync(app.DeviceId, app.Key, false);
        }

        return Strings.Format(Strings.OsdAppVolumeFormat, lead.Name, VolumeMath.ToPercent(volume));
    }
}
