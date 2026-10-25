using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Sdk.Services;

namespace Kastom.Core.Catalog;

/// <summary>Кеш перевіряється тим самим <paramref name="verifier"/> при кожному запуску.</summary>
public sealed class CatalogService : ICatalogService
{
    private readonly ICatalogSource _source;
    private readonly KastomPaths _paths;
    private readonly string _cacheRoot;
    private readonly IModuleLogger _log;
    private readonly object _lock = new();
    private ModuleCatalog? _current;
    private byte[]? _currentRaw;
    private DateTimeOffset? _lastChecked;

    public CatalogService(ICatalogSource source, CatalogVerifier verifier, KastomPaths paths, IModuleLogger log)
    {
        _source = source;
        _paths = paths;
        // Каталог розробника (локальна тека, file://) має окремий кеш: інакше свіжіший локальний каталог
        // заблокував би справжній як «старіший» через захист від підміни старим каталогом.
        _cacheRoot = source.AllowsFileUrls ? paths.CatalogCacheRoot + "-dev" : paths.CatalogCacheRoot;
        _log = log;
        (_current, _currentRaw) = LoadCache(verifier);
    }

    public ModuleCatalog? Current
    {
        get { lock (_lock) return _current; }
    }

    public DateTimeOffset? LastChecked
    {
        get { lock (_lock) return _lastChecked; }
    }

    public bool AllowsFileUrls => _source.AllowsFileUrls;

    public event EventHandler? CatalogChanged;

    public async Task<CatalogFetchResult> RefreshAsync(CancellationToken cancellationToken)
    {
        CatalogFetchResult result;
        try
        {
            result = await _source.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock) _lastChecked = DateTimeOffset.Now;
        }

        switch (result.Status)
        {
            case CatalogFetchStatus.Ok when result is { Catalog: not null, RawCatalog: not null, Signature: not null }:
                bool changed;
                lock (_lock)
                {
                    if (IsRollback(_current, _currentRaw, result.Catalog, result.RawCatalog))
                    {
                        _log.Warn($"Отримано старіший каталог ({result.Catalog.GeneratedAt:u}), ніж уже прийнятий ({_current!.GeneratedAt:u}); його відкинуто.");
                        return new CatalogFetchResult { Status = CatalogFetchStatus.Failed, Error = RollbackError };
                    }

                    WriteCache(result.RawCatalog, result.Signature);
                    changed = _current is null || !SameModules(_current, result.Catalog);
                    _current = result.Catalog;
                    _currentRaw = result.RawCatalog;
                }

                if (changed) CatalogChanged?.Invoke(this, EventArgs.Empty);
                break;
            case CatalogFetchStatus.Ok:
                _log.Warn("Джерело каталогу повернуло Ok без даних; попередній каталог збережено.");
                break;
            case CatalogFetchStatus.NotModified:
                break;
            default:
                _log.Warn($"Каталог не оновлено ({result.Status}): {result.Error}");
                break;
        }

        return result;
    }

    private const string RollbackError = "Отримано старіший каталог, ніж уже прийнятий; його відкинуто.";

    /// <summary>Старіший за прийнятий каталог — підсунутий давній підпис; такий самий час із іншими байтами — теж.</summary>
    private static bool IsRollback(ModuleCatalog? current, byte[]? currentRaw, ModuleCatalog incoming, byte[] incomingRaw)
    {
        if (current is null) return false;
        if (incoming.GeneratedAt < current.GeneratedAt) return true;
        return incoming.GeneratedAt == current.GeneratedAt && currentRaw is not null && !incomingRaw.AsSpan().SequenceEqual(currentRaw);
    }

    private (ModuleCatalog? Catalog, byte[]? Raw) LoadCache(CatalogVerifier verifier)
    {
        var catalogPath = Path.Combine(_cacheRoot, ModuleCatalog.FileName);
        var signaturePath = Path.Combine(_cacheRoot, ModuleCatalog.SignatureFileName);
        try
        {
            if (!File.Exists(catalogPath) || !File.Exists(signaturePath)) return (null, null);
            if (new FileInfo(catalogPath).Length > CatalogVerifier.MaxCatalogSize) throw new CatalogValidationException("Кеш каталогу завеликий.");
            var bytes = File.ReadAllBytes(catalogPath);
            var signature = File.ReadAllText(signaturePath).Trim();
            return (verifier.Verify(bytes, signature, _source.AllowsFileUrls), bytes);
        }
        catch (Exception ex) when (ex is CatalogValidationException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Кеш каталогу проігноровано: {ex.Message}");
            return (null, null);
        }
    }

    private void WriteCache(byte[] catalog, string signature)
    {
        try
        {
            Directory.CreateDirectory(_cacheRoot);
            // Підпис пишеться останнім: обірваний запис дає пару, що не збігається, і кеш просто ігнорується.
            WriteAtomically(Path.Combine(_cacheRoot, ModuleCatalog.FileName), catalog);
            WriteAtomically(Path.Combine(_cacheRoot, ModuleCatalog.SignatureFileName), System.Text.Encoding.UTF8.GetBytes(signature));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Кеш каталогу не записано: {ex.Message}");
        }
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, data);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool SameModules(ModuleCatalog left, ModuleCatalog right) =>
        Keys(left).SetEquals(Keys(right));

    private static HashSet<string> Keys(ModuleCatalog catalog) =>
        catalog.Modules.Select(m => m.Id + "\n" + m.Version).ToHashSet(StringComparer.Ordinal);
}
