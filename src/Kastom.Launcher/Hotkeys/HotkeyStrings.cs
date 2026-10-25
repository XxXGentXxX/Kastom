using System.Globalization;
using System.Resources;

namespace Kastom.Launcher.Hotkeys;

/// <summary>
/// Тексти сторінки «Гарячі клавіші» (власник — E3).
/// Кожен новий рядок у HotkeyStrings.resx отримує тут властивість з тим самим ім'ям.
/// </summary>
public static class HotkeyStrings
{
    private static readonly ResourceManager Manager = new("Kastom.Launcher.Hotkeys.HotkeyStrings", typeof(HotkeyStrings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string PageTitle => Get(nameof(PageTitle));

    public static string EnabledToggle => Get(nameof(EnabledToggle));

    public static string LauncherSection => Get(nameof(LauncherSection));

    public static string StatusActive => Get(nameof(StatusActive));

    public static string StatusConflict => Get(nameof(StatusConflict));

    public static string StatusConflictUnknown => Get(nameof(StatusConflictUnknown));

    public static string StatusTakenBySystem => Get(nameof(StatusTakenBySystem));

    public static string StatusUnassigned => Get(nameof(StatusUnassigned));

    public static string StatusDefaultTaken => Get(nameof(StatusDefaultTaken));

    public static string StatusDisabled => Get(nameof(StatusDisabled));

    public static string ChangeButton => Get(nameof(ChangeButton));

    public static string CancelButton => Get(nameof(CancelButton));

    public static string ResetButton => Get(nameof(ResetButton));

    public static string KeyboardHint => Get(nameof(KeyboardHint));

    public static string FilterInfo => Get(nameof(FilterInfo));

    public static string FilterClear => Get(nameof(FilterClear));

    public static string NoBindingsForKey => Get(nameof(NoBindingsForKey));

    public static string NoActions => Get(nameof(NoActions));

    public static string TooltipLine => Get(nameof(TooltipLine));

    public static string ClearButton => Get(nameof(ClearButton));

    public static string CaptureKeyPrompt => Get(nameof(CaptureKeyPrompt));

    public static string CaptureKeyHint => Get(nameof(CaptureKeyHint));

    public static string AddressLabel => Get(nameof(AddressLabel));

    public static string AddressHint => Get(nameof(AddressHint));

    public static string AddressResetButton => Get(nameof(AddressResetButton));

    public static string AddressAltGrSuffix => Get(nameof(AddressAltGrSuffix));

    public static string AddressCautionSuffix => Get(nameof(AddressCautionSuffix));

    public static string AddressCtrlAltNote => Get(nameof(AddressCtrlAltNote));

    public static string AddressSingleWarning => Get(nameof(AddressSingleWarning));
}
