namespace Kastom.Core;

/// <summary>
/// Де лаунчер тримає файли. Бінарні файли модулів і логи — локально (%LocalAppData%\Kastom),
/// налаштування й дані модулів — у %AppData%\Kastom. Для тестів корені передаються явно.
/// Velopack оновлює лише %LocalAppData%\Kastom\current, тож сусідні папки переживають оновлення лаунчера.
/// </summary>
public sealed class KastomPaths(string localRoot, string roamingRoot)
{
    public static KastomPaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kastom"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kastom"));

    public string LocalRoot { get; } = localRoot;

    public string RoamingRoot { get; } = roamingRoot;

    /// <summary>Встановлені модулі: modules\&lt;id&gt;\&lt;version&gt;\.</summary>
    public string ModulesRoot => Path.Combine(LocalRoot, "modules");

    /// <summary>Тимчасові завантаження; вміст можна стерти будь-коли.</summary>
    public string DownloadsRoot => Path.Combine(LocalRoot, "downloads");

    public string LogsRoot => Path.Combine(LocalRoot, "logs");

    /// <summary>Останній перевірений каталог, щоб показувати модулі без інтернету.</summary>
    public string CatalogCacheRoot => Path.Combine(LocalRoot, "catalog-cache");

    public string SettingsRoot => Path.Combine(RoamingRoot, "settings");

    public string LauncherStateFile => Path.Combine(SettingsRoot, "launcher.json");

    public string ModuleSettingsFile(string moduleId) => Path.Combine(SettingsRoot, "modules", moduleId + ".json");

    public string ModuleDataDirectory(string moduleId) => Path.Combine(RoamingRoot, "modules", moduleId);

    public string ModuleInstallDirectory(string moduleId, string version) => Path.Combine(ModulesRoot, moduleId, version);
}
