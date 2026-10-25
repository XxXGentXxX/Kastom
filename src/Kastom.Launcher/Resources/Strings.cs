using System.Globalization;
using System.Resources;

namespace Kastom.Launcher.Resources;

/// <summary>
/// Тексти головного вікна, сторінок «Модулі» й «Лаунчер», значка в треї (власник — E4a).
/// Кожен новий рядок у Strings.resx отримує тут властивість з тим самим ім'ям,
/// щоб у XAML писати {x:Static res:Strings.AppName}.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("Kastom.Launcher.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string AppName => Get(nameof(AppName));

    public static string MainWindowTitle => Get(nameof(MainWindowTitle));

    public static string TrayTooltip => Get(nameof(TrayTooltip));

    public static string TrayOpen => Get(nameof(TrayOpen));

    public static string TrayQuickPanel => Get(nameof(TrayQuickPanel));

    public static string TrayWidget => Get(nameof(TrayWidget));

    public static string TrayCheckUpdates => Get(nameof(TrayCheckUpdates));

    public static string TrayExit => Get(nameof(TrayExit));

    public static string BalloonUpdatesTitle => Get(nameof(BalloonUpdatesTitle));

    public static string BalloonLauncherVersion => Get(nameof(BalloonLauncherVersion));

    public static string BalloonUpdatesText => Get(nameof(BalloonUpdatesText));

    public static string ActionOpenTitle => Get(nameof(ActionOpenTitle));

    public static string ActionQuickPanelTitle => Get(nameof(ActionQuickPanelTitle));

    public static string ActionToggleWidgetTitle => Get(nameof(ActionToggleWidgetTitle));

    public static string ActionCheckUpdatesTitle => Get(nameof(ActionCheckUpdatesTitle));

    public static string UpdateCheckDone => Get(nameof(UpdateCheckDone));

    public static string UnhandledError => Get(nameof(UnhandledError));

    public static string StartupProblems => Get(nameof(StartupProblems));

    public static string StartupFatal => Get(nameof(StartupFatal));

    public static string QuickPanelFailed => Get(nameof(QuickPanelFailed));

    public static string RestartFailed => Get(nameof(RestartFailed));

    public static string RenderFailed => Get(nameof(RenderFailed));

    public static string NavModules => Get(nameof(NavModules));

    public static string NavModuleSettings => Get(nameof(NavModuleSettings));

    public static string NavHotkeys => Get(nameof(NavHotkeys));

    public static string NavLauncher => Get(nameof(NavLauncher));

    public static string NavEnabledModules => Get(nameof(NavEnabledModules));

    public static string PageUnavailable => Get(nameof(PageUnavailable));

    public static string CheckUpdates => Get(nameof(CheckUpdates));

    public static string Checking => Get(nameof(Checking));

    public static string LastCheckNever => Get(nameof(LastCheckNever));

    public static string LastCheckFormat => Get(nameof(LastCheckFormat));

    public static string CatalogCount => Get(nameof(CatalogCount));

    public static string CatalogNotLoaded => Get(nameof(CatalogNotLoaded));

    public static string CatalogFailed => Get(nameof(CatalogFailed));

    public static string CatalogInvalidSignature => Get(nameof(CatalogInvalidSignature));

    public static string RestartBanner => Get(nameof(RestartBanner));

    public static string Restart => Get(nameof(Restart));

    public static string ModulesLoadError => Get(nameof(ModulesLoadError));

    public static string NoModules => Get(nameof(NoModules));

    public static string StatusEnabled => Get(nameof(StatusEnabled));

    public static string StatusDisabled => Get(nameof(StatusDisabled));

    public static string StatusNotInstalled => Get(nameof(StatusNotInstalled));

    public static string StatusUpdate => Get(nameof(StatusUpdate));

    public static string StatusIncompatible => Get(nameof(StatusIncompatible));

    public static string IncompatibleReason => Get(nameof(IncompatibleReason));

    public static string StatusError => Get(nameof(StatusError));

    public static string StatusRestart => Get(nameof(StatusRestart));

    public static string VersionsInstalled => Get(nameof(VersionsInstalled));

    public static string VersionsAvailable => Get(nameof(VersionsAvailable));

    public static string VersionsSeparator => Get(nameof(VersionsSeparator));

    public static string VersionsIncompatibleNote => Get(nameof(VersionsIncompatibleNote));

    public static string Install => Get(nameof(Install));

    public static string Update => Get(nameof(Update));

    public static string Enable => Get(nameof(Enable));

    public static string Disable => Get(nameof(Disable));

    public static string Rollback => Get(nameof(Rollback));

    public static string Uninstall => Get(nameof(Uninstall));

    public static string ConfirmTitle => Get(nameof(ConfirmTitle));

    public static string UninstallConfirm => Get(nameof(UninstallConfirm));

    public static string OperationFailed => Get(nameof(OperationFailed));

    public static string OperationNeedsRestart => Get(nameof(OperationNeedsRestart));

    public static string SettingsVersion => Get(nameof(SettingsVersion));

    public static string SettingsStartupHeader => Get(nameof(SettingsStartupHeader));

    public static string SettingsAutostart => Get(nameof(SettingsAutostart));

    public static string AutostartFailed => Get(nameof(AutostartFailed));

    public static string SettingsUpdatesHeader => Get(nameof(SettingsUpdatesHeader));

    public static string SettingsUpdateNotify => Get(nameof(SettingsUpdateNotify));

    public static string SettingsUpdateAutomatic => Get(nameof(SettingsUpdateAutomatic));

    public static string SettingsCheckNow => Get(nameof(SettingsCheckNow));

    public static string SettingsUpdateLauncher => Get(nameof(SettingsUpdateLauncher));

    public static string SettingsUpdatingLauncher => Get(nameof(SettingsUpdatingLauncher));

    public static string SettingsLauncherUpdateFailed => Get(nameof(SettingsLauncherUpdateFailed));

    public static string SettingsTaskbarHeader => Get(nameof(SettingsTaskbarHeader));

    public static string SettingsPin => Get(nameof(SettingsPin));

    public static string SettingsPinNote => Get(nameof(SettingsPinNote));

    public static string PinDone => Get(nameof(PinDone));

    public static string PinNotDone => Get(nameof(PinNotDone));

    public static string PinFailed => Get(nameof(PinFailed));

    public static string SettingsWidgetHeader => Get(nameof(SettingsWidgetHeader));

    public static string SettingsWidget => Get(nameof(SettingsWidget));

    public static string SettingsLogsHeader => Get(nameof(SettingsLogsHeader));

    public static string SettingsOpenLogs => Get(nameof(SettingsOpenLogs));

    public static string LogsOpenFailed => Get(nameof(LogsOpenFailed));

    public static string SettingsCatalogHeader => Get(nameof(SettingsCatalogHeader));

    public static string CatalogDevFolder => Get(nameof(CatalogDevFolder));

    public static string CatalogCurrentUrl => Get(nameof(CatalogCurrentUrl));

    public static string CatalogUrlHint => Get(nameof(CatalogUrlHint));

    public static string CatalogApply => Get(nameof(CatalogApply));

    public static string CatalogApplyNote => Get(nameof(CatalogApplyNote));

    public static string CatalogInvalidUrl => Get(nameof(CatalogInvalidUrl));

    public static string BalloonModulesRestart => Get(nameof(BalloonModulesRestart));

    public static string UpdaterNotInstalled => Get(nameof(UpdaterNotInstalled));

    public static string UpdaterFileNameMismatch => Get(nameof(UpdaterFileNameMismatch));

    public static string UpdaterHashMismatch => Get(nameof(UpdaterHashMismatch));
}
