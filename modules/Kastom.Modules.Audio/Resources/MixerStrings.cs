using System.Globalization;
using System.Resources;

namespace Kastom.Modules.Audio.Resources;

/// <summary>Тексти мікшера. Кожен рядок у MixerStrings.resx отримує тут властивість з тим самим ім'ям.</summary>
internal static class MixerStrings
{
    private static readonly ResourceManager Manager = new("Kastom.Modules.Audio.Resources.MixerStrings", typeof(MixerStrings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    public static string OutputSection => Get(nameof(OutputSection));
    public static string InputSection => Get(nameof(InputSection));
    public static string NoOutputs => Get(nameof(NoOutputs));
    public static string NoInputs => Get(nameof(NoInputs));
    public static string AudioUnavailable => Get(nameof(AudioUnavailable));
    public static string FlyoutTitle => Get(nameof(FlyoutTitle));
    public static string CollapseAll => Get(nameof(CollapseAll));
    public static string ExpandAll => Get(nameof(ExpandAll));

    public static string BadgeDefault => Get(nameof(BadgeDefault));
    public static string BadgeCommunications => Get(nameof(BadgeCommunications));
    public static string MakeDefault => Get(nameof(MakeDefault));
    public static string MakeCommunications => Get(nameof(MakeCommunications));
    public static string MakeDefaultTooltipFormat => Get(nameof(MakeDefaultTooltipFormat));
    public static string MakeCommunicationsTooltipFormat => Get(nameof(MakeCommunicationsTooltipFormat));
    public static string MakeDefaultNameFormat => Get(nameof(MakeDefaultNameFormat));
    public static string MakeCommunicationsNameFormat => Get(nameof(MakeCommunicationsNameFormat));
    public static string PercentFormat => Get(nameof(PercentFormat));
    public static string VolumeNameFormat => Get(nameof(VolumeNameFormat));
    public static string MuteNameFormat => Get(nameof(MuteNameFormat));
    public static string UnmuteNameFormat => Get(nameof(UnmuteNameFormat));
    public static string MuteTooltip => Get(nameof(MuteTooltip));
    public static string UnmuteTooltip => Get(nameof(UnmuteTooltip));
    public static string MoreNameFormat => Get(nameof(MoreNameFormat));
    public static string MoreTooltip => Get(nameof(MoreTooltip));

    public static string MenuOutput => Get(nameof(MenuOutput));
    public static string MenuOutputDefault => Get(nameof(MenuOutputDefault));
    public static string MenuAssign => Get(nameof(MenuAssign));
    public static string MenuAppSlotFormat => Get(nameof(MenuAppSlotFormat));
    public static string MenuMicSlotFormat => Get(nameof(MenuMicSlotFormat));
    public static string SlotNotSet => Get(nameof(SlotNotSet));
    public static string MenuOpenApp => Get(nameof(MenuOpenApp));
    public static string MenuRevealApp => Get(nameof(MenuRevealApp));
    public static string MenuOpenAppFormat => Get(nameof(MenuOpenAppFormat));
    public static string MenuSoundSettings => Get(nameof(MenuSoundSettings));

    public static string TileSound => Get(nameof(TileSound));
    public static string TileMics => Get(nameof(TileMics));
    public static string TileOn => Get(nameof(TileOn));
    public static string TileOff => Get(nameof(TileOff));
    public static string TileSoundMuteName => Get(nameof(TileSoundMuteName));
    public static string TileSoundUnmuteName => Get(nameof(TileSoundUnmuteName));
    public static string TileMicsMuteName => Get(nameof(TileMicsMuteName));
    public static string TileMicsUnmuteName => Get(nameof(TileMicsUnmuteName));
    public static string TileSoundToggleName => Get(nameof(TileSoundToggleName));
    public static string TileMicsToggleName => Get(nameof(TileMicsToggleName));

    public static string MicSettings => Get(nameof(MicSettings));
    public static string MicLoading => Get(nameof(MicLoading));
    public static string MicBoost => Get(nameof(MicBoost));
    public static string MicBoostValueFormat => Get(nameof(MicBoostValueFormat));
    public static string MicBoostUnavailable => Get(nameof(MicBoostUnavailable));
    public static string MicListen => Get(nameof(MicListen));
    public static string MicListenThrough => Get(nameof(MicListenThrough));
    public static string MicListenDefaultOutput => Get(nameof(MicListenDefaultOutput));
    public static string MicWindowsSettings => Get(nameof(MicWindowsSettings));

    public static string LinkSoundSettings => Get(nameof(LinkSoundSettings));
    public static string LinkAppVolume => Get(nameof(LinkAppVolume));
    public static string LinkTaskbar => Get(nameof(LinkTaskbar));
}
