namespace Kastom.Core.Models;

/// <summary>
/// Каталог модулів: один файл catalog.json у репозиторії на GitHub і поруч catalog.json.sig —
/// підпис ECDSA P-256 (SHA-256, формат IEEE P1363, base64) над точними байтами catalog.json.
/// </summary>
public sealed class ModuleCatalog
{
    public const string FileName = "catalog.json";
    public const string SignatureFileName = "catalog.json.sig";

    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Час збирання каталогу. Лаунчер відкидає каталог, старіший за вже прийнятий: інакше старий,
    /// але справжній підписаний каталог можна було б підсунути замість нового.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<CatalogEntry> Modules { get; init; } = [];

    /// <summary>
    /// Підписаний опис поточного релізу лаунчера. Самооновлення ставить лише реліз саме цієї версії
    /// і лише після того, як SHA-256 завантаженого пакета збігся з <see cref="LauncherRelease.Sha256"/>.
    /// Null — оновлювати лаунчер не можна.
    /// </summary>
    public LauncherRelease? Launcher { get; init; }
}

/// <summary>Реліз лаунчера, яким його публікує Velopack (повний пакет *-full.nupkg).</summary>
public sealed class LauncherRelease
{
    public required string Version { get; init; }

    /// <summary>SHA-256 повного пакета Velopack, 64 шістнадцяткові символи в нижньому регістрі.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Ім'я файлу повного пакета, наприклад "Kastom-0.2.0-full.nupkg".</summary>
    public required string FileName { get; init; }
}

/// <summary>Один модуль у каталозі: найновіша версія й звідки її взяти.</summary>
public sealed class CatalogEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required string Version { get; init; }

    public required int ContractVersion { get; init; }

    public string MinLauncherVersion { get; init; } = "0.1.0";

    public ModuleRunMode RunMode { get; init; } = ModuleRunMode.InProcess;

    /// <summary>Адреса zip-пакета модуля. Лише https:// (file:// — тільки для локального каталогу розробника).</summary>
    public required string Url { get; init; }

    /// <summary>SHA-256 zip-пакета, 64 шістнадцяткові символи в нижньому регістрі.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Розмір zip-пакета в байтах.</summary>
    public long Size { get; init; }

    public string? Changelog { get; init; }
}
