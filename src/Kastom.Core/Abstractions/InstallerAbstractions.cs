using Kastom.Core.Models;

namespace Kastom.Core.Abstractions;

public sealed class InstallResult
{
    public required bool Success { get; init; }

    public ModuleManifest? Manifest { get; init; }

    public string? Error { get; init; }

    public static InstallResult Ok(ModuleManifest manifest) => new() { Success = true, Manifest = manifest };

    public static InstallResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Завантаження й розпакування модулів. Нічого не запускає: лише кладе перевірені файли в
/// <see cref="KastomPaths.ModuleInstallDirectory"/>.
/// </summary>
public interface IModuleInstaller
{
    /// <summary>
    /// Завантажити пакет за <see cref="CatalogEntry.Url"/>, звірити розмір і SHA-256, безпечно розпакувати,
    /// перевірити module.json (id і версія збігаються з каталогом, контракт сумісний) і покласти в папку версії.
    /// Якщо ця версія вже стоїть — повернути її маніфест без завантаження.
    /// </summary>
    Task<InstallResult> InstallAsync(CatalogEntry entry, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Маніфест встановленої версії або null, якщо папки чи файлу немає.</summary>
    ModuleManifest? ReadInstalledManifest(string moduleId, string version);

    void RemoveVersion(string moduleId, string version);

    void RemoveModule(string moduleId);
}
