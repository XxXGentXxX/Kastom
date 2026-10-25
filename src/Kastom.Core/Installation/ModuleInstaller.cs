using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kastom.Core.Abstractions;
using Kastom.Core.Catalog;
using Kastom.Core.Models;
using Kastom.Sdk.Services;

namespace Kastom.Core.Installation;

public sealed class ModuleInstaller(
    HttpClient http,
    KastomPaths paths,
    IModuleLogger log,
    ModuleVersion launcherVersion,
    bool allowFileUrls) : IModuleInstaller
{
    private const int MaxEntries = 10_000;
    private const long MaxUnpackedBytes = 1024L * 1024 * 1024;
    private const int MaxManifestBytes = 1024 * 1024;
    private const int BufferSize = 81920;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _staleStagingCleaned;

    /// <summary>Скільки пакет може не слати жодного байта, перш ніж завантаження скасується. Змінюється лише в тестах.</summary>
    internal TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public async Task<InstallResult> InstallAsync(CatalogEntry entry, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_staleStagingCleaned)
            {
                _staleStagingCleaned = true;
                CleanStaleStaging();
            }

            return await InstallCoreAsync(entry, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InstallFailedException ex)
        {
            log.Warn($"Модуль '{entry.Id}' не встановлено: {ex.Message}");
            return InstallResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            log.Error($"Модуль '{entry.Id}' не встановлено.", ex);
            return InstallResult.Fail($"Не вдалося встановити модуль: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public ModuleManifest? ReadInstalledManifest(string moduleId, string version)
    {
        if (!ModuleManifest.IsValidId(moduleId) || !IsValidVersion(version)) return null;
        try
        {
            var directory = paths.ModuleInstallDirectory(moduleId, version);
            var manifest = ReadManifest(directory);
            return manifest is not null && CheckManifest(manifest, directory, moduleId, version, null) is null ? manifest : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InstallFailedException)
        {
            return null;
        }
    }

    public void RemoveVersion(string moduleId, string version)
    {
        if (!ModuleManifest.IsValidId(moduleId))
            throw new ArgumentException($"Неправильний id модуля: '{moduleId}'.", nameof(moduleId));
        if (!IsValidVersion(version))
            throw new ArgumentException($"Неправильна версія: '{version}'.", nameof(version));

        DeleteInsideModulesRoot(paths.ModuleInstallDirectory(moduleId, version));
    }

    public void RemoveModule(string moduleId)
    {
        if (!ModuleManifest.IsValidId(moduleId))
            throw new ArgumentException($"Неправильний id модуля: '{moduleId}'.", nameof(moduleId));

        DeleteInsideModulesRoot(Path.Combine(paths.ModulesRoot, moduleId));
    }

    private async Task<InstallResult> InstallCoreAsync(CatalogEntry entry, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var problem = CatalogEntryValidator.Validate(entry, allowFileUrls);
        if (problem is not null) return InstallResult.Fail(problem);

        var compatibility = ModuleCompatibility.Check(entry.ContractVersion, entry.MinLauncherVersion, launcherVersion);
        if (!compatibility.IsCompatible) return InstallResult.Fail(compatibility.Reason!);

        var installDirectory = paths.ModuleInstallDirectory(entry.Id, entry.Version);
        var existing = ReadInstalledManifest(entry.Id, entry.Version);
        if (existing is not null && existing.ContractVersion == entry.ContractVersion)
            return InstallResult.Ok(existing);
        if (Directory.Exists(installDirectory))
        {
            log.Warn($"Папка модуля '{entry.Id}' {entry.Version} пошкоджена або не збігається з каталогом; видаляю й ставлю заново.");
            Directory.Delete(installDirectory, recursive: true);
        }

        Directory.CreateDirectory(paths.DownloadsRoot);
        var zipPath = Path.Combine(paths.DownloadsRoot, $"{entry.Id}-{entry.Version}-{Guid.NewGuid():N}.zip");
        string? staging = null;
        try
        {
            var hash = await DownloadAsync(entry, zipPath, progress, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InstallFailedException("Контрольна сума пакета не збігається з каталогом. Пакет відхилено.");

            staging = Path.Combine(paths.ModulesRoot, entry.Id, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            await ExtractAsync(zipPath, staging, cancellationToken).ConfigureAwait(false);

            var manifest = ReadManifest(staging) ?? throw new InstallFailedException("У пакеті немає module.json.");
            var manifestProblem = CheckManifest(manifest, staging, entry.Id, entry.Version, entry.ContractVersion);
            if (manifestProblem is not null) throw new InstallFailedException(manifestProblem);

            Directory.Move(staging, installDirectory);
            staging = null;
            log.Info($"Модуль '{entry.Id}' {entry.Version} встановлено.");
            return InstallResult.Ok(manifest);
        }
        finally
        {
            TryDelete(zipPath);
            if (staging is not null) TryDeleteDirectory(staging);
        }
    }

    /// <summary>Завантажує пакет у zipPath й повертає його SHA-256 (шістнадцяткові символи).</summary>
    private async Task<string> DownloadAsync(CatalogEntry entry, string zipPath, IProgress<double>? progress, CancellationToken callerToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        idle.CancelAfter(IdleTimeout);
        var cancellationToken = idle.Token;
        try
        {
            return await DownloadCoreAsync(entry, zipPath, progress, idle, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new InstallFailedException($"Завантаження пакета зупинилося: понад {IdleTimeout.TotalSeconds:0} с не надходило даних.");
        }
    }

    private async Task<string> DownloadCoreAsync(CatalogEntry entry, string zipPath, IProgress<double>? progress, CancellationTokenSource idle, CancellationToken cancellationToken)
    {
        var url = new Uri(entry.Url);
        HttpResponseMessage? response = null;
        Stream source;
        long? total;
        try
        {
            if (url.Scheme == Uri.UriSchemeFile)
            {
                if (!allowFileUrls) throw new InstallFailedException("Адреси file:// дозволено лише для локального каталогу розробника.");
                if (url.IsUnc) throw new InstallFailedException("Мережеві адреси file:// не підтримуються.");
                var file = new FileInfo(url.LocalPath);
                if (!file.Exists) throw new InstallFailedException($"Файл пакета не знайдено: {file.FullName}.");
                total = file.Length;
                source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            }
            else if (url.Scheme == Uri.UriSchemeHttps)
            {
                response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InstallFailedException($"Сервер пакета відповів кодом {(int)response.StatusCode}.");
                total = response.Content.Headers.ContentLength;
                source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new InstallFailedException("Пакет можна завантажувати лише за адресою https://.");
            }

            await using (source)
            {
                if (total > CatalogEntryValidator.MaxPackageSize)
                    throw new InstallFailedException("Пакет завеликий: понад 512 МБ.");

                var expected = entry.Size > 0 ? entry.Size : total ?? 0;
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                long count = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    idle.CancelAfter(IdleTimeout);
                    count += read;
                    if (count > CatalogEntryValidator.MaxPackageSize)
                        throw new InstallFailedException("Пакет завеликий: понад 512 МБ.");
                    if (entry.Size > 0 && count > entry.Size)
                        throw new InstallFailedException("Розмір пакета більший, ніж указано в каталозі.");
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (expected > 0) progress?.Report(Math.Min(1.0, (double)count / expected));
                }

                if (entry.Size > 0 && count != entry.Size)
                    throw new InstallFailedException("Розмір пакета не збігається з каталогом.");
                progress?.Report(1.0);
                return Convert.ToHexStringLower(hasher.GetHashAndReset());
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Розпаковує zip у staging із захистом від zip-slip і «бомб».</summary>
    private static async Task ExtractAsync(string zipPath, string staging, CancellationToken cancellationToken)
    {
        var stagingFull = Path.GetFullPath(staging);
        var prefix = stagingFull.EndsWith(Path.DirectorySeparatorChar) ? stagingFull : stagingFull + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries)
            throw new InstallFailedException($"У пакеті забагато файлів (понад {MaxEntries}).");

        long declared = 0;
        foreach (var entry in archive.Entries)
        {
            declared += entry.Length;
            if (declared > MaxUnpackedBytes)
                throw new InstallFailedException("Пакет після розпакування завеликий (понад 1 ГБ).");
        }

        long written = 0;
        var buffer = new byte[BufferSize];
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.Contains("..") || name.Contains(':'))
                throw new InstallFailedException($"Небезпечне ім'я файлу в пакеті: '{name}'.");

            if (HasReservedSegment(name))
                throw new InstallFailedException($"Недопустиме ім'я файлу в пакеті (зарезервоване в Windows або з крапкою чи пробілом наприкінці): '{name}'.");

            var target = Path.GetFullPath(Path.Combine(stagingFull, name));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InstallFailedException($"Файл пакета виходить за межі папки встановлення: '{name}'.");

            if (entry.Name.Length == 0) continue; // запис-папка

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            long entryBytes = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                entryBytes += read;
                written += read;
                if (entryBytes > entry.Length || written > MaxUnpackedBytes)
                    throw new InstallFailedException("Розпакований вміст пакета більший, ніж заявлено.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            if (entryBytes != entry.Length)
                throw new InstallFailedException($"Файл '{name}' у пакеті пошкоджений.");
        }
    }

    /// <summary>Сегмент імені — пристрій Windows (CON, NUL, COM1…, також з розширенням) або закінчується крапкою чи пробілом.</summary>
    private static bool HasReservedSegment(string name)
    {
        foreach (var segment in name.Split('/', '\\'))
        {
            if (segment.Length == 0) continue;
            if (segment[^1] is '.' or ' ') return true;
            var dot = segment.IndexOf('.');
            var stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
            if (ReservedNames.Contains(stem)) return true;
        }

        return false;
    }

    /// <summary>Читає module.json з папки; null, якщо файлу немає.</summary>
    private static ModuleManifest? ReadManifest(string directory)
    {
        var path = Path.Combine(directory, ModuleManifest.FileName);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxManifestBytes)
            throw new InstallFailedException("module.json завеликий.");
        try
        {
            return JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllBytes(path), KastomJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InstallFailedException($"module.json пошкоджений: {ex.Message}");
        }
    }

    /// <summary>Повертає опис проблеми українською або null. expectedContract null — контракт не звіряти.</summary>
    private static string? CheckManifest(ModuleManifest manifest, string directory, string expectedId, string expectedVersion, int? expectedContract)
    {
        if (!ModuleManifest.IsValidId(manifest.Id))
            return $"module.json: неправильний id '{manifest.Id}'.";
        if (manifest.Id != expectedId)
            return $"module.json: id '{manifest.Id}' не збігається з каталогом ('{expectedId}').";
        if (!ModuleVersion.TryParse(manifest.Version, out var version) || version.CompareTo(ModuleVersion.Parse(expectedVersion)) != 0)
            return $"module.json: версія '{manifest.Version}' не збігається з каталогом ('{expectedVersion}').";
        if (expectedContract is not null && manifest.ContractVersion != expectedContract)
            return $"module.json: версія контракту {manifest.ContractVersion} не збігається з каталогом ({expectedContract}).";
        if (string.IsNullOrWhiteSpace(manifest.EntryType))
            return "module.json: порожній entryType.";

        var assembly = manifest.EntryAssembly;
        if (string.IsNullOrWhiteSpace(assembly) || assembly is "." or ".." || assembly != Path.GetFileName(assembly)
            || assembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return $"module.json: entryAssembly має бути іменем файлу без шляху: '{assembly}'.";
        if (!File.Exists(Path.Combine(directory, assembly)))
            return $"У пакеті немає збірки '{assembly}', указаної в module.json.";

        return null;
    }

    private static bool IsValidVersion(string? version) =>
        CatalogEntryValidator.IsSafeVersion(version) && ModuleVersion.TryParse(version, out _);

    private void DeleteInsideModulesRoot(string directory)
    {
        var root = Path.GetFullPath(paths.ModulesRoot);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Шлях виходить за межі папки модулів.");

        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    /// <summary>Видаляє залишки обірваних установок: modules\<id>\.staging-*. Помилки ігноруються.</summary>
    private void CleanStaleStaging()
    {
        try
        {
            var root = Path.GetFullPath(paths.ModulesRoot);
            if (!Directory.Exists(root)) return;
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            foreach (var moduleDirectory in Directory.GetDirectories(root))
            {
                foreach (var staging in Directory.GetDirectories(moduleDirectory, ".staging-*"))
                {
                    var full = Path.GetFullPath(staging);
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    TryDeleteDirectory(full);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Не вдалося прибрати залишки установок: {ex.Message}");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Не вдалося видалити тимчасовий файл '{path}': {ex.Message}");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Не вдалося видалити тимчасову папку '{path}': {ex.Message}");
        }
    }

    private sealed class InstallFailedException(string message) : Exception(message);
}
