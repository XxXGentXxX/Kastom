using System.Globalization;
using System.Resources;

namespace Kastom.Modules.Audio.Resources;

/// <summary>
/// Тексти налаштувань, дій, підказок і значків модуля. Кожен рядок у Strings.resx отримує тут властивість
/// з тим самим ім'ям; тексти мікшера — у <c>MixerStrings</c>.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Manager = new("Kastom.Modules.Audio.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    public static string ModuleTitle => Get(nameof(ModuleTitle));
    public static string SystemSounds => Get(nameof(SystemSounds));
    public static string UnknownApp => Get(nameof(UnknownApp));
    public static string UnknownDevice => Get(nameof(UnknownDevice));
    public static string WidgetTitle => Get(nameof(WidgetTitle));

    public static string OpenMixerTitle => Get(nameof(OpenMixerTitle));
    public static string GroupDevices => Get(nameof(GroupDevices));
    public static string SeparateCommunicationsTitle => Get(nameof(SeparateCommunicationsTitle));
    public static string SeparateCommunicationsDescription => Get(nameof(SeparateCommunicationsDescription));
    public static string GroupMixer => Get(nameof(GroupMixer));
    public static string CompactTitle => Get(nameof(CompactTitle));
    public static string CompactDescription => Get(nameof(CompactDescription));
    public static string ShowMetersTitle => Get(nameof(ShowMetersTitle));
    public static string ShowMetersDescription => Get(nameof(ShowMetersDescription));
    public static string VolumeStepTitle => Get(nameof(VolumeStepTitle));
    public static string GroupTray => Get(nameof(GroupTray));
    public static string TrayIconTitle => Get(nameof(TrayIconTitle));
    public static string TrayIconDescription => Get(nameof(TrayIconDescription));
    public static string TrayMicBadgeTitle => Get(nameof(TrayMicBadgeTitle));
    public static string TrayMicBadgeDescription => Get(nameof(TrayMicBadgeDescription));
    public static string PinTrayTitle => Get(nameof(PinTrayTitle));
    public static string PinTrayDescription => Get(nameof(PinTrayDescription));
    public static string GroupHints => Get(nameof(GroupHints));
    public static string OsdTitle => Get(nameof(OsdTitle));
    public static string GroupExperimental => Get(nameof(GroupExperimental));
    public static string PerAppOutputTitle => Get(nameof(PerAppOutputTitle));
    public static string PerAppOutputDescription => Get(nameof(PerAppOutputDescription));
    public static string ResetAppOutputsTitle => Get(nameof(ResetAppOutputsTitle));
    public static string GroupSlots => Get(nameof(GroupSlots));
    public static string AppSlotTitleFormat => Get(nameof(AppSlotTitleFormat));
    public static string AppSlotDescription => Get(nameof(AppSlotDescription));
    public static string MicSlotTitleFormat => Get(nameof(MicSlotTitleFormat));
    public static string MicSlotDescription => Get(nameof(MicSlotDescription));

    public static string ActionToggleMic => Get(nameof(ActionToggleMic));
    public static string ActionToggleAllMics => Get(nameof(ActionToggleAllMics));
    public static string ActionNextMic => Get(nameof(ActionNextMic));
    public static string ActionMicSlotFormat => Get(nameof(ActionMicSlotFormat));
    public static string ActionToggleOutput => Get(nameof(ActionToggleOutput));
    public static string ActionNextOutput => Get(nameof(ActionNextOutput));
    public static string ActionPrevOutput => Get(nameof(ActionPrevOutput));
    public static string ActionVolumeUp => Get(nameof(ActionVolumeUp));
    public static string ActionVolumeDown => Get(nameof(ActionVolumeDown));
    public static string ActionToggleActiveApp => Get(nameof(ActionToggleActiveApp));
    public static string ActionActiveAppVolumeUp => Get(nameof(ActionActiveAppVolumeUp));
    public static string ActionActiveAppVolumeDown => Get(nameof(ActionActiveAppVolumeDown));
    public static string ActionAppSlotToggleFormat => Get(nameof(ActionAppSlotToggleFormat));
    public static string ActionAppSlotUpFormat => Get(nameof(ActionAppSlotUpFormat));
    public static string ActionAppSlotDownFormat => Get(nameof(ActionAppSlotDownFormat));
    public static string ActionOpenMixer => Get(nameof(ActionOpenMixer));
    public static string ActionResetAppOutputs => Get(nameof(ActionResetAppOutputs));
    public static string ActionOpenTaskbarSettings => Get(nameof(ActionOpenTaskbarSettings));
    public static string ActionActiveAppDescription => Get(nameof(ActionActiveAppDescription));
    public static string ActionAppSlotDescriptionFormat => Get(nameof(ActionAppSlotDescriptionFormat));
    public static string ActionMicSlotDescriptionFormat => Get(nameof(ActionMicSlotDescriptionFormat));

    public static string OsdMicMuted => Get(nameof(OsdMicMuted));
    public static string OsdMicUnmuted => Get(nameof(OsdMicUnmuted));
    public static string OsdAllMicsMuted => Get(nameof(OsdAllMicsMuted));
    public static string OsdAllMicsUnmuted => Get(nameof(OsdAllMicsUnmuted));
    public static string OsdNamedMutedFormat => Get(nameof(OsdNamedMutedFormat));
    public static string OsdNamedUnmutedFormat => Get(nameof(OsdNamedUnmutedFormat));
    public static string OsdMicSwitchedFormat => Get(nameof(OsdMicSwitchedFormat));
    public static string OsdOutputSwitchedFormat => Get(nameof(OsdOutputSwitchedFormat));
    public static string OsdVolumeFormat => Get(nameof(OsdVolumeFormat));
    public static string OsdOutputMuted => Get(nameof(OsdOutputMuted));
    public static string OsdOutputUnmuted => Get(nameof(OsdOutputUnmuted));
    public static string OsdAppMutedFormat => Get(nameof(OsdAppMutedFormat));
    public static string OsdAppUnmutedFormat => Get(nameof(OsdAppUnmutedFormat));
    public static string OsdAppVolumeFormat => Get(nameof(OsdAppVolumeFormat));
    public static string OsdNoActiveApp => Get(nameof(OsdNoActiveApp));
    public static string OsdAppSlotEmptyFormat => Get(nameof(OsdAppSlotEmptyFormat));
    public static string OsdAppNotPlayingFormat => Get(nameof(OsdAppNotPlayingFormat));
    public static string OsdMicSlotEmptyFormat => Get(nameof(OsdMicSlotEmptyFormat));
    public static string OsdMicNotFoundFormat => Get(nameof(OsdMicNotFoundFormat));
    public static string OsdNoOutputs => Get(nameof(OsdNoOutputs));
    public static string OsdNoMics => Get(nameof(OsdNoMics));
    public static string OsdAudioUnavailable => Get(nameof(OsdAudioUnavailable));
    public static string OsdAppOutputsReset => Get(nameof(OsdAppOutputsReset));
    public static string OsdAppRoutingFailed => Get(nameof(OsdAppRoutingFailed));
    public static string OsdDefaultSwitchFailed => Get(nameof(OsdDefaultSwitchFailed));
    public static string OsdCommandFailed => Get(nameof(OsdCommandFailed));
    public static string OsdOpenAppFailed => Get(nameof(OsdOpenAppFailed));

    public static string TrayVolumeTooltipFormat => Get(nameof(TrayVolumeTooltipFormat));
    public static string TrayMutedTooltipFormat => Get(nameof(TrayMutedTooltipFormat));
    public static string TrayNoDevice => Get(nameof(TrayNoDevice));
    public static string TrayMicOnTooltipFormat => Get(nameof(TrayMicOnTooltipFormat));
    public static string TrayMicOffTooltipFormat => Get(nameof(TrayMicOffTooltipFormat));
    public static string TrayMenuMixer => Get(nameof(TrayMenuMixer));
    public static string TrayMenuSoundSettings => Get(nameof(TrayMenuSoundSettings));
    public static string TrayMenuAppVolume => Get(nameof(TrayMenuAppVolume));
    public static string TrayMenuMicMute => Get(nameof(TrayMenuMicMute));
    public static string TrayMenuMicUnmute => Get(nameof(TrayMenuMicUnmute));
    public static string TrayMenuMicSettings => Get(nameof(TrayMenuMicSettings));
}
