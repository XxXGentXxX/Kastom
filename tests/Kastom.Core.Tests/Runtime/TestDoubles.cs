using System.IO;
using System.Text.Json;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Services;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Core.Tests.Runtime;

/// <summary>Тимчасова папка, яка стирається разом з тестом (що вдасться: завантажені dll Windows тримає).</summary>
internal sealed class TempDir : IDisposable
{
    private static readonly string BaseRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kastom-runtime-tests");

    static TempDir()
    {
        // Залишки минулих запусків: там були завантажені збірки, які не вдалося стерти одразу.
        try
        {
            foreach (var old in Directory.GetDirectories(BaseRoot))
            {
                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public TempDir()
    {
        _path = System.IO.Path.Combine(BaseRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_path);
    }

    private readonly string _path;

    public string FullPath => _path;

    public KastomPaths Paths => new(System.IO.Path.Combine(_path, "local"), System.IO.Path.Combine(_path, "roaming"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_path, recursive: true);
        }
        catch
        {
        }
    }
}

internal sealed class InlineUi : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }
}

internal sealed class ListLogger : IModuleLogger
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines) return _lines.ToArray();
        }
    }

    public void Info(string message) => Add("INFO", message);

    public void Warn(string message) => Add("WARN", message);

    public void Error(string message, Exception? exception = null) => Add("ERROR", message);

    private void Add(string level, string message)
    {
        lock (_lines) _lines.Add($"[{level}] {message}");
    }
}

internal sealed class FakeNotifications : INotificationService
{
    public List<string> Messages { get; } = [];

    public void ShowOsd(string text) => Messages.Add(text);
}

internal sealed class FakeCatalogService : ICatalogService
{
    private int _refreshCalls;

    public ModuleCatalog? Current { get; set; }

    public DateTimeOffset? LastChecked { get; set; }

    public bool AllowsFileUrls => true;

    public int RefreshCalls => Volatile.Read(ref _refreshCalls);

    /// <summary>Що виконати всередині RefreshAsync (затримка, виняток).</summary>
    public Func<Task>? OnRefresh { get; set; }

    public event EventHandler? CatalogChanged;

    public async Task<CatalogFetchResult> RefreshAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        if (OnRefresh is not null) await OnRefresh();
        return new CatalogFetchResult { Status = CatalogFetchStatus.Ok, Catalog = Current };
    }

    public void RaiseChanged() => CatalogChanged?.Invoke(this, EventArgs.Empty);

    public static CatalogEntry Entry(string id, string name, string version) => new()
    {
        Id = id,
        Name = name,
        Version = version,
        ContractVersion = 1,
        Url = "https://example.invalid/" + id + ".zip",
        Sha256 = new string('0', 64),
    };

    public static ModuleCatalog Catalog(params CatalogEntry[] entries) => new() { Modules = entries };
}

/// <summary>
/// Підроблений установник: «встановлення» копіює готову збірку в папку версії, а module.json
/// переписує під версію з каталогу. Читає маніфести з диска, як справжній.
/// </summary>
internal sealed class FakeInstaller(KastomPaths paths, string? sourceDirectory) : IModuleInstaller
{
    public List<string> Installed { get; } = [];

    public List<(string Id, string Version)> RemovedVersions { get; } = [];

    public List<string> RemovedModules { get; } = [];

    /// <summary>Що виконати всередині InstallAsync (наприклад, зависнути, тримаючи чергу операцій).</summary>
    public Func<Task>? OnInstall { get; set; }

    public async Task<InstallResult> InstallAsync(CatalogEntry entry, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Installed.Add($"{entry.Id}@{entry.Version}");
        if (OnInstall is not null) await OnInstall();
        if (sourceDirectory is null) return InstallResult.Fail("Немає джерела.");

        var target = paths.ModuleInstallDirectory(entry.Id, entry.Version);
        if (!Directory.Exists(target)) ModuleFiles.Copy(sourceDirectory, target);
        ModuleFiles.RewriteManifest(target, entry.Id, entry.Name, entry.Version);
        return InstallResult.Ok(ReadInstalledManifest(entry.Id, entry.Version)!);
    }

    public ModuleManifest? ReadInstalledManifest(string moduleId, string version)
    {
        var file = Path.Combine(paths.ModuleInstallDirectory(moduleId, version), ModuleManifest.FileName);
        return File.Exists(file) ? JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(file), KastomJson.Options) : null;
    }

    public void RemoveVersion(string moduleId, string version)
    {
        RemovedVersions.Add((moduleId, version));
        var directory = paths.ModuleInstallDirectory(moduleId, version);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    // Файли не стираються: збірку, завантажену в тест, Windows однаково не відпустить.
    public void RemoveModule(string moduleId) => RemovedModules.Add(moduleId);
}

internal static class ModuleFiles
{
    public static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
    }

    public static void RewriteManifest(string directory, string id, string name, string version, string? entryType = null, string? runMode = null)
    {
        var file = Path.Combine(directory, ModuleManifest.FileName);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        node["id"] = id;
        node["name"] = name;
        node["version"] = version;
        if (entryType is not null) node["entryType"] = entryType;
        if (runMode is not null) node["runMode"] = runMode;
        File.WriteAllText(file, node.ToJsonString());
    }
}

internal sealed class FakeLauncherUpdater : ILauncherUpdater
{
    public bool IsInstalled { get; set; }

    public string? NewVersion { get; set; }

    public int ApplyCalls { get; private set; }

    public Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken) => Task.FromResult(NewVersion);

    public Task DownloadAndRestartAsync(CancellationToken cancellationToken)
    {
        ApplyCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeModuleManager : IModuleManager
{
    public List<ModuleInfo> ModuleList { get; } = [];

    public List<string> Updated { get; } = [];

    public Action<string>? OnUpdate { get; set; }

    public IReadOnlyList<ModuleInfo> Modules => ModuleList;

    public event EventHandler? Changed { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<OperationResult> InstallAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> UpdateAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Updated.Add(moduleId);
        OnUpdate?.Invoke(moduleId);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> UninstallAsync(string moduleId) => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> EnableAsync(string moduleId) => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> DisableAsync(string moduleId) => Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> RollbackAsync(string moduleId) => Task.FromResult(OperationResult.Ok());

    public ISettingsRegistry? GetSettings(string moduleId) => null;

    public IReadOnlyList<WidgetDefinition> GetWidgets(string moduleId) => [];
}

/// <summary>Модулі, які навмисно ламаються. Тести копіюють збірку тестів у папку «встановленого» модуля.</summary>
public sealed class ThrowingModule : Kastom.Sdk.IKastomModule
{
    public void Configure(Kastom.Sdk.IModuleContext context) => throw new InvalidOperationException("Configure зламався");

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class HangingModule : Kastom.Sdk.IKastomModule
{
    public void Configure(Kastom.Sdk.IModuleContext context)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>StartAsync блокує потік синхронно, не повертаючи керування.</summary>
public sealed class BlockingStartModule : Kastom.Sdk.IKastomModule
{
    public void Configure(Kastom.Sdk.IModuleContext context)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Thread.Sleep(2500);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Запускається нормально, а StopAsync блокує потік.</summary>
public sealed class BlockingStopModule : Kastom.Sdk.IKastomModule
{
    public void Configure(Kastom.Sdk.IModuleContext context)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Thread.Sleep(2500);
        return Task.CompletedTask;
    }
}
