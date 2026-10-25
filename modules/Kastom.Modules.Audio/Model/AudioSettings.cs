using Kastom.Modules.Audio.Resources;
using Kastom.Sdk.Settings;

namespace Kastom.Modules.Audio.Model;

/// <summary>Ключі налаштувань модуля.</summary>
public static class SettingKeys
{
    public const string SeparateCommunications = "separate-communications";
    public const string ShowMeters = "show-meters";
    public const string VolumeStep = "volume-step";
    public const string TrayIcon = "tray-icon";
    public const string MicTrayIcon = "mic-tray-icon";
    public const string PinTrayIcons = "pin-tray-icons";
    public const string Osd = "osd";
    public const string PerAppOutput = "per-app-output";
    public const string ResetAppOutputs = "reset-app-outputs";
    public const string OpenMixer = "open-mixer";

    public static string AppSlot(int slot) => $"app-slot-{slot}";

    public static string MicSlot(int slot) => $"mic-slot-{slot}";
}

/// <summary>Ідентифікатори дій модуля (повний id у лаунчері — "audio/&lt;id&gt;").</summary>
public static class ActionIds
{
    public const int AppSlotCount = 3;
    public const int MicSlotCount = 2;

    public const string ToggleMic = "toggle-mic";
    public const string ToggleAllMics = "toggle-all-mics";
    public const string NextMic = "next-mic";
    public const string ToggleOutput = "toggle-output";
    public const string NextOutput = "next-output";
    public const string PrevOutput = "prev-output";
    public const string VolumeUp = "volume-up";
    public const string VolumeDown = "volume-down";
    public const string ToggleActiveApp = "toggle-active-app";
    public const string ActiveAppVolumeUp = "active-app-volume-up";
    public const string ActiveAppVolumeDown = "active-app-volume-down";
    public const string OpenMixer = "open-mixer";
    public const string ResetAppOutputs = "reset-app-outputs";
    public const string OpenTaskbarSettings = "open-taskbar-settings";

    public static string MicSlot(int slot) => $"mic-{slot}";

    public static string AppSlotToggle(int slot) => $"app-{slot}";

    public static string AppSlotUp(int slot) => $"app-{slot}-up";

    public static string AppSlotDown(int slot) => $"app-{slot}-down";
}

/// <summary>Опис налаштувань модуля для лаунчера.</summary>
public static class AudioSettings
{
    public static void Define(ISettingsRegistry settings)
    {
        settings.Define(new ButtonSetting
        {
            Key = SettingKeys.OpenMixer,
            Title = Strings.OpenMixerTitle,
            ActionId = ActionIds.OpenMixer,
            IsQuick = true,
        });

        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.SeparateCommunications,
            Title = Strings.SeparateCommunicationsTitle,
            Description = Strings.SeparateCommunicationsDescription,
            Group = Strings.GroupDevices,
            Default = false,
        });

        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.ShowMeters,
            Title = Strings.ShowMetersTitle,
            Description = Strings.ShowMetersDescription,
            Group = Strings.GroupMixer,
            Default = true,
        });
        settings.Define(new RangeSetting
        {
            Key = SettingKeys.VolumeStep,
            Title = Strings.VolumeStepTitle,
            Group = Strings.GroupMixer,
            Min = 1,
            Max = 20,
            Step = 1,
            Default = 2,
            Unit = "%",
        });

        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.TrayIcon,
            Title = Strings.TrayIconTitle,
            Description = Strings.TrayIconDescription,
            Group = Strings.GroupTray,
            Default = true,
        });
        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.MicTrayIcon,
            Title = Strings.MicTrayIconTitle,
            Description = Strings.MicTrayIconDescription,
            Group = Strings.GroupTray,
            Default = true,
        });
        settings.Define(new ButtonSetting
        {
            Key = SettingKeys.PinTrayIcons,
            Title = Strings.PinTrayTitle,
            Description = Strings.PinTrayDescription,
            Group = Strings.GroupTray,
            ActionId = ActionIds.OpenTaskbarSettings,
        });

        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.Osd,
            Title = Strings.OsdTitle,
            Group = Strings.GroupHints,
            Default = true,
        });

        settings.Define(new ToggleSetting
        {
            Key = SettingKeys.PerAppOutput,
            Title = Strings.PerAppOutputTitle,
            Description = Strings.PerAppOutputDescription,
            Group = Strings.GroupExperimental,
            Default = true,
        });
        settings.Define(new ButtonSetting
        {
            Key = SettingKeys.ResetAppOutputs,
            Title = Strings.ResetAppOutputsTitle,
            Group = Strings.GroupExperimental,
            ActionId = ActionIds.ResetAppOutputs,
        });

        for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
        {
            settings.Define(new TextSetting
            {
                Key = SettingKeys.AppSlot(slot),
                Title = Strings.Format(Strings.AppSlotTitleFormat, slot),
                Description = slot == 1 ? Strings.AppSlotDescription : null,
                Group = Strings.GroupSlots,
            });
        }

        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++)
        {
            settings.Define(new TextSetting
            {
                Key = SettingKeys.MicSlot(slot),
                Title = Strings.Format(Strings.MicSlotTitleFormat, slot),
                Description = slot == 1 ? Strings.MicSlotDescription : null,
                Group = Strings.GroupSlots,
            });
        }
    }
}

/// <summary>
/// Поточні значення налаштувань модуля з типами. Подія <see cref="Changed"/> може прийти з будь-якого потоку.
/// </summary>
public sealed class AudioOptions
{
    private readonly ISettingsRegistry _settings;

    public AudioOptions(ISettingsRegistry settings)
    {
        _settings = settings;
        _settings.Changed += (_, e) => Changed?.Invoke(this, e.Key);
    }

    /// <summary>Змінилося налаштування з цим ключем (<see cref="SettingKeys"/>).</summary>
    public event EventHandler<string>? Changed;

    public bool SeparateCommunications => _settings.GetBool(SettingKeys.SeparateCommunications);

    public bool ShowMeters => _settings.GetBool(SettingKeys.ShowMeters);

    public int VolumeStepPercent => (int)Math.Round(_settings.GetDouble(SettingKeys.VolumeStep));

    public bool TrayIcon => _settings.GetBool(SettingKeys.TrayIcon);

    public bool MicTrayIcon => _settings.GetBool(SettingKeys.MicTrayIcon);

    public bool Osd => _settings.GetBool(SettingKeys.Osd);

    public bool PerAppOutput => _settings.GetBool(SettingKeys.PerAppOutput);

    /// <summary>Ім'я процесу для дії «Програма N» (без ".exe", обрізане); "" — не вибрано.</summary>
    public string AppSlot(int slot) => Normalize(_settings.GetString(SettingKeys.AppSlot(slot)), stripExe: true);

    /// <summary>Частина назви мікрофона для дії «Мікрофон N»; "" — не вибрано.</summary>
    public string MicSlot(int slot) => Normalize(_settings.GetString(SettingKeys.MicSlot(slot)), stripExe: false);

    public void SetAppSlot(int slot, string processName) => _settings.Set(SettingKeys.AppSlot(slot), processName);

    public void SetMicSlot(int slot, string deviceName) => _settings.Set(SettingKeys.MicSlot(slot), deviceName);

    /// <summary>
    /// Ролі для кнопки вибору пристрою за замовчуванням. Без «окремо» одна кнопка ставить усі ролі;
    /// з «окремо» звичайна кнопка — Console і Multimedia, кнопка «для зв'язку» — Communications.
    /// </summary>
    public DefaultRoles RolesFor(bool communicationsButton) =>
        !SeparateCommunications ? DefaultRoles.All
        : communicationsButton ? DefaultRoles.Communications
        : DefaultRoles.Normal;

    private static string Normalize(string value, bool stripExe)
    {
        var trimmed = value.Trim();
        if (stripExe && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];
        return trimmed;
    }
}
