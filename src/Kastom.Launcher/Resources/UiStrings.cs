using System.Globalization;
using System.Resources;

namespace Kastom.Launcher.Resources;

/// <summary>
/// Тексти налаштувань, віджетів, швидкої панелі й підказок (власник — E4b).
/// Кожен новий рядок у UiStrings.resx отримує тут властивість з тим самим ім'ям.
/// </summary>
public static class UiStrings
{
    private static readonly ResourceManager Manager = new("Kastom.Launcher.Resources.UiStrings", typeof(UiStrings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string OpenLauncher => Get(nameof(OpenLauncher));
    public static string SettingsSaveErrorFormat => Get(nameof(SettingsSaveErrorFormat));
    public static string ActionFailedFormat => Get(nameof(ActionFailedFormat));
    public static string ModuleHasNoSettings => Get(nameof(ModuleHasNoSettings));
    public static string AggregateTitle => Get(nameof(AggregateTitle));
    public static string ModulePageLink => Get(nameof(ModulePageLink));
    public static string AggregateEmpty => Get(nameof(AggregateEmpty));
    public static string VersionFormat => Get(nameof(VersionFormat));
    public static string SettingsSection => Get(nameof(SettingsSection));
    public static string WidgetsSection => Get(nameof(WidgetsSection));
    public static string ActionsSection => Get(nameof(ActionsSection));
    public static string WidgetOnDesktop => Get(nameof(WidgetOnDesktop));
    public static string RunAction => Get(nameof(RunAction));
    public static string ModuleNotFound => Get(nameof(ModuleNotFound));
    public static string ModuleNotInstalled => Get(nameof(ModuleNotInstalled));
    public static string ModuleDisabled => Get(nameof(ModuleDisabled));
    public static string ModuleNotRunning => Get(nameof(ModuleNotRunning));
    public static string ModuleErrorFormat => Get(nameof(ModuleErrorFormat));
    public static string ModuleNoWidgets => Get(nameof(ModuleNoWidgets));
    public static string ModuleNoActions => Get(nameof(ModuleNoActions));
    public static string CustomPageErrorFormat => Get(nameof(CustomPageErrorFormat));
    public static string WidgetContentErrorFormat => Get(nameof(WidgetContentErrorFormat));
    public static string WidgetClose => Get(nameof(WidgetClose));
    public static string LauncherWidgetTitle => Get(nameof(LauncherWidgetTitle));
    public static string QuickEmpty => Get(nameof(QuickEmpty));
    public static string QuickSettings => Get(nameof(QuickSettings));
    public static string QuickQuickSettings => Get(nameof(QuickQuickSettings));
    public static string QuickStatusRunning => Get(nameof(QuickStatusRunning));
    public static string QuickStatusDisabled => Get(nameof(QuickStatusDisabled));
    public static string QuickStatusNotRunning => Get(nameof(QuickStatusNotRunning));
    public static string QuickToggleFailedFormat => Get(nameof(QuickToggleFailedFormat));
    public static string QuickRestartNote => Get(nameof(QuickRestartNote));
    public static string ModuleTabModule => Get(nameof(ModuleTabModule));
    public static string ModuleTabSettings => Get(nameof(ModuleTabSettings));
    public static string HotkeysSection => Get(nameof(HotkeysSection));
}
