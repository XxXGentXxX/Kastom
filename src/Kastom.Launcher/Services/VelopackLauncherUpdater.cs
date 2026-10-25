using System.IO;
using System.Security.Cryptography;
using Kastom.Core;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Launcher.Resources;
using Kastom.Sdk.Services;
using Velopack;
using Velopack.Sources;

namespace Kastom.Launcher.Services;

/// <summary>
/// Оновлення лаунчера через Velopack (релізи на GitHub). Довіряємо не GitHub, а підписаному каталогу:
/// оновлення пропонується лише тоді, коли знайдений Velopack-реліз збігається з <c>catalog.Current.Launcher</c>,
/// а завантажений пакет перед застосуванням перевіряється за SHA-256 з каталогу.
/// Реліз шукаємо не стрічкою релізів GitHub, а за тегом <c>v{версія з каталогу}</c>: GithubSource читає лише першу
/// сторінку (10 релізів), а релізи модулів (<c>&lt;id&gt;-vX.Y.Z</c>) лежать у тому самому репозиторії й витіснили б реліз лаунчера.
/// <paramref name="shutdownForUpdate"/> закриває лаунчер по-людськи (зупиняє модулі): ApplyUpdatesAndRestart
/// вийшов би з процесу миттєво, не давши модулям зупинитися.
/// </summary>
public sealed class VelopackLauncherUpdater(
    ICatalogService catalog,
    ModuleVersion currentVersion,
    IModuleLogger log,
    Func<Task> shutdownForUpdate,
    bool selfUpdateAllowed = true) : ILauncherUpdater
{
    // Поки репозиторій не створено, в адресі лишається заглушка OWNER: мережу не чіпаємо взагалі.
    private static readonly bool Disabled =
        KastomEndpoints.GitHubRepoUrl.Contains("OWNER", StringComparison.Ordinal);

    private PackagesUpdateManager? _manager;
    private string? _managerVersion;
    private UpdateInfo? _pending;
    private LauncherRelease? _pendingRelease;
    private bool _disabledLogged;

    /// <summary>Менеджер останньої перевіреної версії; для IsInstalled і теки пакетів джерело неважливе.</summary>
    private PackagesUpdateManager Manager => _manager ?? ManagerFor(currentVersion.ToString());

    /// <summary>Джерело — файли релізу з тегом v{версія}: releases.win.json і пакети поруч.</summary>
    private PackagesUpdateManager ManagerFor(string version)
    {
        if (_manager is null || _managerVersion != version)
        {
            var feed = $"{KastomEndpoints.GitHubRepoUrl}/releases/download/v{Uri.EscapeDataString(version)}/";
            _manager = new PackagesUpdateManager(new SimpleWebSource(feed));
            _managerVersion = version;
        }

        return _manager;
    }

    public bool IsInstalled
    {
        get
        {
            if (Disabled || !selfUpdateAllowed)
            {
                if (!_disabledLogged)
                {
                    _disabledLogged = true;
                    log.Info(selfUpdateAllowed
                        ? "Самооновлення вимкнено: адреса репозиторію ще не задана (OWNER)."
                        : "Самооновлення вимкнено: лаунчер працює в пісочниці (--data-dir).");
                }

                return false;
            }

            return Manager.IsInstalled;
        }
    }

    public async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        _pending = null;
        _pendingRelease = null;
        if (!IsInstalled) return null;

        // Без підписаного опису релізу в каталозі лаунчер не оновлюємо.
        if (catalog.Current?.Launcher is not { } release) return null;
        if (!ModuleVersion.TryParse(release.Version, out var offered) || !(offered > currentVersion)) return null;

        var info = await ManagerFor(release.Version).CheckForUpdatesAsync().WaitAsync(cancellationToken);
        if (info is null) return null;

        var target = info.TargetFullRelease;
        var found = target.Version.ToString();
        if (!ModuleVersion.TryParse(found, out var foundVersion) || foundVersion.CompareTo(offered) != 0)
        {
            log.Warn($"Velopack знайшов {found}, а каталог пропонує {release.Version}: оновлення не пропонуємо.");
            return null;
        }

        _pending = info;
        _pendingRelease = release;
        return found;
    }

    public async Task DownloadAndRestartAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled) throw new InvalidOperationException(Strings.UpdaterNotInstalled);

        if (_pending is null) await CheckForUpdateAsync(cancellationToken);
        if (_pending is not { } update || _pendingRelease is not { } release) return;

        var target = update.TargetFullRelease;
        if (!string.Equals(target.FileName, release.FileName, StringComparison.Ordinal))
        {
            log.Error($"Ім'я пакета {target.FileName} не збігається з каталогом ({release.FileName}).");
            throw new InvalidOperationException(Strings.UpdaterFileNameMismatch);
        }

        await Manager.DownloadUpdatesAsync(update, null, cancellationToken);
        await VerifyDownloadedPackageAsync(target.FileName, release.Sha256, cancellationToken);

        // Оновлювач чекає завершення процесу (до 60 с), застосовує оновлення й запускає лаунчер знову.
        Manager.WaitExitThenApplyUpdates(target, false, true);
        await shutdownForUpdate();
    }

    /// <summary>SHA-256 завантаженого повного пакета має збігтися з підписаним у каталозі; інакше файл видаляється.</summary>
    private async Task VerifyDownloadedPackageAsync(string fileName, string expectedSha256, CancellationToken cancellationToken)
    {
        var packagesDir = Manager.PackagesDirectory
            ?? throw new InvalidOperationException(Strings.UpdaterHashMismatch);
        var path = Path.Combine(packagesDir, Path.GetFileName(fileName));

        string actual;
        try
        {
            await using var stream = File.OpenRead(path);
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"Не вдалося прочитати завантажений пакет {path}", exception);
            throw new InvalidOperationException(Strings.UpdaterHashMismatch, exception);
        }

        if (string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)) return;

        log.Error($"SHA-256 пакета {fileName} ({actual}) не збігається з каталогом ({expectedSha256}): файл видалено.");
        try { File.Delete(path); }
        catch (Exception exception) { log.Warn($"Не вдалося видалити пакет: {exception.Message}"); }
        _pending = null;
        _pendingRelease = null;
        throw new InvalidOperationException(Strings.UpdaterHashMismatch);
    }
}

/// <summary>UpdateManager, що показує папку, куди Velopack кладе завантажені пакети (Locator у базового класу захищений).</summary>
internal sealed class PackagesUpdateManager(IUpdateSource source) : UpdateManager(source)
{
    public string? PackagesDirectory => Locator.PackagesDir;
}
