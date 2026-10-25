using System.Reflection;
using Kastom.Core.Abstractions;
using Kastom.Core.Installation;
using Kastom.Core.Models;
using Kastom.Sdk;
using Kastom.Sdk.Services;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Core.Runtime;

public sealed class ModuleManager(
    KastomPaths paths,
    ILauncherStateStore state,
    ICatalogService catalog,
    IModuleInstaller installer,
    ActionCatalog actions,
    INotificationService notifications,
    IUiDispatcher ui,
    Func<string, IModuleLogger> loggerFactory,
    ModuleVersion launcherVersion) : IModuleManager
{
    private const int MaxReasonLength = 120;

    // Усі публічні операції йдуть по черзі.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Скасовується при виході (StopAllAsync): операції, що чекають у черзі, не затримують закриття.
    private readonly CancellationTokenSource _shutdown = new();

    // Замок для стану лаунчера й службових словників; тримається недовго, без очікувань усередині.
    private readonly object _sync = new();
    private readonly Dictionary<string, ModuleRuntimeState> _runtime = new();
    private readonly Dictionary<(string Id, string Version), ModuleManifest> _manifests = new();
    private readonly IModuleLogger _log = loggerFactory("launcher");
    private EventHandler? _changed;
    private bool _catalogHooked;

    /// <summary>Перевірка сумісності; у тестах підмінюється.</summary>
    internal Func<int, string, ModuleVersion, CompatibilityResult> CompatibilityCheck { get; set; } = ModuleCompatibility.Check;

    /// <summary>Скільки чекати Configure, StartAsync і StopAsync одного модуля.</summary>
    internal TimeSpan ModuleTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Скільки чекати зупинки одного модуля при виході.</summary>
    internal TimeSpan StopAllTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public event EventHandler? Changed
    {
        add
        {
            lock (_sync)
            {
                _changed += value;
                if (_catalogHooked) return;
                _catalogHooked = true;
                catalog.CatalogChanged += (_, _) => RaiseChanged();
            }
        }
        remove
        {
            lock (_sync) _changed -= value;
        }
    }

    public IReadOnlyList<ModuleInfo> Modules
    {
        get
        {
            Dictionary<string, InstalledModuleState> installed;
            lock (_sync)
            {
                installed = state.State.Modules.ToDictionary(
                    pair => pair.Key,
                    pair => new InstalledModuleState
                    {
                        Version = pair.Value.Version,
                        PreviousVersion = pair.Value.PreviousVersion,
                        Enabled = pair.Value.Enabled,
                    });
            }

            var entries = catalog.Current?.Modules.ToDictionary(e => e.Id) ?? new Dictionary<string, CatalogEntry>();
            var ids = installed.Keys.Concat(entries.Keys).Distinct();
            return ids.Select(id => BuildInfo(id, installed.GetValueOrDefault(id), entries.GetValueOrDefault(id)))
                .OrderBy(m => m.Name, StringComparer.CurrentCulture)
                .ToList();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            await _gate.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            RemovePendingModules();

            string[] installedIds;
            lock (_sync) installedIds = state.State.Modules.Keys.ToArray();
            foreach (var id in installedIds) RemoveStaleVersions(id);

            string[] enabledIds;
            lock (_sync)
            {
                enabledIds = state.State.Modules.Where(pair => pair.Value.Enabled).Select(pair => pair.Key)
                    .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }

            foreach (var id in enabledIds)
            {
                linked.Token.ThrowIfCancellationRequested();
                await LoadAndStartAsync(id, linked.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Лаунчер закривається: решту модулів не запускаємо.
        }
        finally
        {
            _gate.Release();
            RaiseChanged();
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        // З цього моменту операції, що чекають своєї черги, скасовуються.
        _shutdown.Cancel();

        var gateTaken = false;
        try
        {
            await _gate.WaitAsync(cancellationToken);
            gateTaken = true;
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Черга операцій з модулями зайнята: зупиняю модулі без неї.");
        }

        try
        {
            KeyValuePair<string, ModuleRuntimeState>[] running;
            lock (_sync) running = _runtime.Where(pair => pair.Value.IsRunning).ToArray();

            // Паралельно, щоб загальний час не залежав від кількості завислих модулів.
            await Task.WhenAll(running.Select(pair => StopModuleAsync(pair.Key, pair.Value, StopAllTimeout, CancellationToken.None)));
        }
        finally
        {
            if (gateTaken) _gate.Release();
            RaiseChanged();
        }
    }

    public Task<OperationResult> InstallAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(token => InstallCoreAsync(moduleId, progress, update: false, token), cancellationToken);

    public Task<OperationResult> UpdateAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(token => InstallCoreAsync(moduleId, progress, update: true, token), cancellationToken);

    public Task<OperationResult> UninstallAsync(string moduleId) =>
        RunExclusiveAsync(_ => UninstallCoreAsync(moduleId), CancellationToken.None);

    public Task<OperationResult> EnableAsync(string moduleId) =>
        RunExclusiveAsync(token => EnableCoreAsync(moduleId, token), CancellationToken.None);

    public Task<OperationResult> DisableAsync(string moduleId) =>
        RunExclusiveAsync(_ => DisableCoreAsync(moduleId), CancellationToken.None);

    public Task<OperationResult> RollbackAsync(string moduleId) =>
        RunExclusiveAsync(token => RollbackCoreAsync(moduleId, token), CancellationToken.None);

    public ISettingsRegistry? GetSettings(string moduleId) => FindRuntime(moduleId)?.Module?.Context.SettingsRegistry;

    public IReadOnlyList<WidgetDefinition> GetWidgets(string moduleId) =>
        FindRuntime(moduleId)?.Module?.Context.WidgetRegistry.Widgets ?? [];

    // Токен користувача й сигнал виходу лаунчера: обидва скасовують і очікування в черзі, і саму операцію.
    private async Task<OperationResult> RunExclusiveAsync(Func<CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            await _gate.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Скасовано.");
        }

        try
        {
            return await operation(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return OperationResult.Fail("Скасовано.");
        }
        catch (Exception ex)
        {
            _log.Error("Операція з модулем завершилась помилкою.", ex);
            return OperationResult.Fail(ShortReason(ex));
        }
        finally
        {
            _gate.Release();
            RaiseChanged();
        }
    }

    private async Task<OperationResult> InstallCoreAsync(string id, IProgress<double>? progress, bool update, CancellationToken cancellationToken)
    {
        var entry = catalog.Current?.Modules.FirstOrDefault(e => e.Id == id);
        if (entry is null) return OperationResult.Fail("Модуля немає в каталозі.");

        var existing = FindInstalled(id);
        if (update && existing is null) return OperationResult.Fail("Модуль ще не встановлено.");

        var compatibility = CompatibilityCheck(entry.ContractVersion, entry.MinLauncherVersion, launcherVersion);
        if (!compatibility.IsCompatible)
            return OperationResult.Fail(compatibility.Reason ?? "Модуль несумісний з цією версією лаунчера.");

        var result = await installer.InstallAsync(entry, progress, cancellationToken);
        if (!result.Success || result.Manifest is null)
            return OperationResult.Fail(result.Error ?? "Не вдалося встановити модуль.");

        var newVersion = result.Manifest.Version;
        Commit(() =>
        {
            if (state.State.Modules.TryGetValue(id, out var current))
            {
                if (current.Version != newVersion) current.PreviousVersion = current.Version;
                current.Version = newVersion;

                // Вимкнений користувачем модуль оновлення не вмикає.
            }
            else
            {
                state.State.Modules[id] = new InstalledModuleState { Version = newVersion, Enabled = true };
            }

            // Модуль, який щойно видалили в цьому запуску й ставлять знову, не можна стирати при наступному старті.
            state.State.PendingRemovals.RemoveAll(p => p == id);
        });
        ForgetManifests(id);

        var runtime = GetRuntime(id);
        runtime.Error = null;
        RemoveStaleVersions(id);
        return await ApplyVersionAsync(id, runtime, cancellationToken);
    }

    private async Task<OperationResult> UninstallCoreAsync(string id)
    {
        var installed = FindInstalled(id);
        if (installed is null) return OperationResult.Fail("Модуль не встановлено.");

        var runtime = GetRuntime(id);
        await StopModuleAsync(id, runtime, ModuleTimeout, CancellationToken.None);

        // Збірку, завантажену в цьому запуску, Windows тримає заблокованою: папку стираємо при наступному старті.
        var assemblyLoaded = runtime.LoadedVersion is not null;
        var deferRemoval = assemblyLoaded;
        if (!deferRemoval)
        {
            try
            {
                installer.RemoveModule(id);
            }
            catch (Exception ex)
            {
                _log.Warn($"Не вдалося видалити файли модуля '{id}' одразу: {ex.Message}");
                deferRemoval = true;
            }
        }

        Commit(() =>
        {
            state.State.Modules.Remove(id);
            if (deferRemoval && !state.State.PendingRemovals.Contains(id)) state.State.PendingRemovals.Add(id);
        });
        ForgetManifests(id);

        runtime.Error = null;
        if (assemblyLoaded) runtime.RestartRequired = true;
        else lock (_sync) _runtime.Remove(id);
        return OperationResult.Ok(deferRemoval);
    }

    private async Task<OperationResult> EnableCoreAsync(string id, CancellationToken cancellationToken)
    {
        if (FindInstalled(id) is null) return OperationResult.Fail("Модуль не встановлено.");

        Commit(() => state.State.Modules[id].Enabled = true);
        var runtime = GetRuntime(id);
        runtime.Error = null;
        return await ApplyVersionAsync(id, runtime, cancellationToken);
    }

    private async Task<OperationResult> DisableCoreAsync(string id)
    {
        if (FindInstalled(id) is null) return OperationResult.Fail("Модуль не встановлено.");

        var runtime = GetRuntime(id);
        await StopModuleAsync(id, runtime, ModuleTimeout, CancellationToken.None);
        runtime.Error = null;
        Commit(() => state.State.Modules[id].Enabled = false);
        return OperationResult.Ok();
    }

    private async Task<OperationResult> RollbackCoreAsync(string id, CancellationToken cancellationToken)
    {
        var installed = FindInstalled(id);
        if (installed is null) return OperationResult.Fail("Модуль не встановлено.");
        if (installed.PreviousVersion is null) return OperationResult.Fail("Немає попередньої версії.");

        var manifest = ReadManifest(id, installed.PreviousVersion);
        if (manifest is null) return OperationResult.Fail("Попередню версію не знайдено на диску.");

        var compatibility = CompatibilityCheck(manifest.ContractVersion, manifest.MinLauncherVersion, launcherVersion);
        if (!compatibility.IsCompatible)
            return OperationResult.Fail(compatibility.Reason ?? "Попередня версія несумісна з цим лаунчером.");

        Commit(() =>
        {
            var current = state.State.Modules[id];
            (current.Version, current.PreviousVersion) = (current.PreviousVersion!, current.Version);
        });

        var runtime = GetRuntime(id);
        runtime.Error = null;
        return await ApplyVersionAsync(id, runtime, cancellationToken);
    }

    /// <summary>
    /// Приводить виконання у відповідність зі станом після зміни версії чи вмикання. Завантажену збірку
    /// підміняти на льоту не можна, тому за іншої версії лишається тільки перезапуск.
    /// </summary>
    private async Task<OperationResult> ApplyVersionAsync(string id, ModuleRuntimeState runtime, CancellationToken cancellationToken)
    {
        var installed = FindInstalled(id);
        if (installed is null) return OperationResult.Ok();

        if (!installed.Enabled) return OperationResult.Ok();

        if (runtime.LoadedVersion is not null && runtime.LoadedVersion != installed.Version)
        {
            runtime.RestartRequired = true;
            return OperationResult.Ok(restartRequired: true);
        }

        runtime.RestartRequired = false;
        if (runtime.IsRunning) return OperationResult.Ok();

        var error = await LoadAndStartAsync(id, cancellationToken);
        return error is null ? OperationResult.Ok() : OperationResult.Fail(error);
    }

    /// <summary>Завантажує (якщо ще не завантажено), налаштовує й запускає модуль. Повертає причину збою або null.</summary>
    private async Task<string?> LoadAndStartAsync(string id, CancellationToken cancellationToken)
    {
        var runtime = GetRuntime(id);
        var name = id;
        var started = false;
        Task? startTask = null;
        try
        {
            var installed = FindInstalled(id) ?? throw new InvalidOperationException("модуль не встановлено");
            var manifest = ReadManifest(id, installed.Version)
                           ?? throw new InvalidOperationException("не вдалося прочитати module.json");
            name = manifest.Name;

            if (runtime.Module is null) await LoadAsync(id, installed.Version, manifest, runtime, cancellationToken);
            var module = runtime.Module!;

            await RunWithTimeoutAsync(token => startTask = Task.Run(() => module.Instance.StartAsync(token)), ModuleTimeout, "запуск модуля", cancellationToken);
            started = true;

            // Дії стають видимими лише для модуля, що запустився.
            actions.SetModuleActions(id, manifest.Name, module.Context.ActionRegistry.Actions);
            runtime.IsRunning = true;
            runtime.Error = null;
            return null;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            if (started) await TryStopAfterFailureAsync(runtime);
            return Fail(id, name, runtime, ex);
        }
        catch (OperationCanceledException) when (startTask is not null)
        {
            // Лаунчер закривається посеред запуску: модуль міг устигнути щось змінити в системі.
            // Даємо запуску до 2 с завершитися й просимо модуль зупинитися.
            try
            {
                await startTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // Запуск упав або ще триває — зупинку все одно просимо.
            }

            await TryStopAfterFailureAsync(runtime);
            throw;
        }
    }

    private async Task LoadAsync(string id, string version, ModuleManifest manifest, ModuleRuntimeState runtime, CancellationToken cancellationToken)
    {
        if (manifest.Id != id) throw new InvalidOperationException("id у module.json не збігається з папкою модуля");
        if (manifest.RunMode == ModuleRunMode.OutOfProcess)
            throw new InvalidOperationException("Модулі в окремому процесі ще не підтримуються");

        var compatibility = CompatibilityCheck(manifest.ContractVersion, manifest.MinLauncherVersion, launcherVersion);
        if (!compatibility.IsCompatible)
            throw new InvalidOperationException(compatibility.Reason ?? "модуль несумісний з цим лаунчером");

        var directory = Path.GetFullPath(paths.ModuleInstallDirectory(id, version));
        var entryPath = Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly));
        if (!entryPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("entryAssembly виходить за межі папки модуля");
        if (!File.Exists(entryPath)) throw new InvalidOperationException($"немає файлу {manifest.EntryAssembly}");

        var loadContext = new ModuleLoadContext(entryPath);
        var assembly = loadContext.LoadFromAssemblyPath(entryPath);
        runtime.LoadedVersion = version;

        var type = assembly.GetType(manifest.EntryType, throwOnError: false)
                   ?? throw new InvalidOperationException($"немає типу {manifest.EntryType}");
        if (!typeof(IKastomModule).IsAssignableFrom(type))
            throw new InvalidOperationException($"тип {manifest.EntryType} не реалізує IKastomModule");

        var instance = (IKastomModule)Activator.CreateInstance(type)!;
        var context = new ModuleContext(id, paths, loggerFactory(id), notifications, ui);

        // Configure — один раз за запуск і в потоці інтерфейсу.
        await RunWithTimeoutAsync(_ => ui.InvokeAsync(() => instance.Configure(context)), ModuleTimeout, "налаштування модуля", cancellationToken);
        runtime.Module = new LoadedModule(instance, context, manifest.Name);
    }

    /// <summary>Прибирає дії, зупиняє модуль (з обмеженням часу), помилку пише в лог. Перевіряти результат не потрібно.</summary>
    private async Task StopModuleAsync(string id, ModuleRuntimeState runtime, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!runtime.IsRunning || runtime.Module is null) return;

        actions.RemoveModuleActions(id);
        runtime.IsRunning = false;
        try
        {
            var module = runtime.Module.Instance;
            await RunWithTimeoutAsync(token => Task.Run(() => module.StopAsync(token)), timeout, "зупинка модуля", cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Error($"Модуль '{id}' не зупинився чисто.", ex);
        }
    }

    private async Task TryStopAfterFailureAsync(ModuleRuntimeState runtime)
    {
        try
        {
            if (runtime.Module is not null)
            {
                var module = runtime.Module.Instance;
                await RunWithTimeoutAsync(token => Task.Run(() => module.StopAsync(token)), ModuleTimeout, "зупинка модуля", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Після збою модуль не зупинився чисто: {ex.Message}");
        }
    }

    /// <summary>Записує помилку, вимикає модуль у стані й показує підказку. Повертає коротку причину.</summary>
    private string Fail(string id, string name, ModuleRuntimeState runtime, Exception exception)
    {
        var reason = ShortReason(exception);
        runtime.IsRunning = false;
        runtime.Error = reason;
        actions.RemoveModuleActions(id);

        try
        {
            Commit(() =>
            {
                if (state.State.Modules.TryGetValue(id, out var installed)) installed.Enabled = false;
            });
        }
        catch (Exception ex)
        {
            _log.Error("Не вдалося зберегти стан після збою модуля.", ex);
        }

        _log.Error($"Модуль '{id}' вимкнено: {reason}", exception);
        try
        {
            notifications.ShowOsd($"Модуль «{name}» вимкнено: {reason}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Не вдалося показати підказку: {ex.Message}");
        }

        return reason;
    }

    private static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> operation, TimeSpan timeout, string what, CancellationToken cancellationToken)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        var task = operation(source.Token);
        try
        {
            await task.WaitAsync(timeout, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            source.Cancel();
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException($"{what}: перевищено {timeout.TotalSeconds:0.#} с");
        }
    }

    private void RemovePendingModules()
    {
        string[] pending;
        lock (_sync) pending = state.State.PendingRemovals.ToArray();
        if (pending.Length == 0) return;

        var done = new List<string>();
        foreach (var id in pending)
        {
            // Модуль, який з того часу встановили знову, не чіпаємо; запис просто прибираємо.
            if (FindInstalled(id) is not null)
            {
                done.Add(id);
                continue;
            }

            try
            {
                installer.RemoveModule(id);
                done.Add(id);
            }
            catch (Exception ex)
            {
                _log.Warn($"Не вдалося стерти папку видаленого модуля '{id}': {ex.Message}");
            }
        }

        if (done.Count > 0) Commit(() => state.State.PendingRemovals.RemoveAll(done.Contains));
    }

    // Лишає тільки поточну й попередню версії та ту, чию збірку завантажено.
    private void RemoveStaleVersions(string id)
    {
        try
        {
            var installed = FindInstalled(id);
            var root = Path.Combine(paths.ModulesRoot, id);
            if (installed is null || !Directory.Exists(root)) return;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { installed.Version };
            if (installed.PreviousVersion is not null) keep.Add(installed.PreviousVersion);
            if (FindRuntime(id)?.LoadedVersion is { } loaded) keep.Add(loaded);

            foreach (var directory in Directory.GetDirectories(root))
            {
                var version = Path.GetFileName(directory);
                if (keep.Contains(version)) continue;
                try
                {
                    installer.RemoveVersion(id, version);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Не вдалося стерти стару версію {id} {version}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Не вдалося прибрати старі версії модуля '{id}': {ex.Message}");
        }
    }

    private ModuleInfo BuildInfo(string id, InstalledModuleState? installed, CatalogEntry? entry)
    {
        var manifest = installed is null ? null : ReadManifest(id, installed.Version);
        var runtime = FindRuntime(id);

        var isCompatible = entry is null
                           || CompatibilityCheck(entry.ContractVersion, entry.MinLauncherVersion, launcherVersion).IsCompatible;
        var hasUpdate = installed is not null && entry is not null && isCompatible
                        && ModuleVersion.TryParse(installed.Version, out var current)
                        && ModuleVersion.TryParse(entry.Version, out var available)
                        && available > current;

        return new ModuleInfo
        {
            Id = id,
            Name = manifest?.Name ?? entry?.Name ?? id,
            Description = manifest?.Description ?? entry?.Description ?? "",
            InstalledVersion = installed?.Version,
            AvailableVersion = entry?.Version,
            PreviousVersion = installed?.PreviousVersion,
            IsEnabled = installed?.Enabled ?? false,
            IsRunning = runtime?.IsRunning ?? false,
            HasUpdate = hasUpdate,
            IsCompatible = isCompatible,
            RestartRequired = runtime?.RestartRequired ?? false,
            Error = runtime?.Error,
            RunMode = manifest?.RunMode ?? entry?.RunMode ?? ModuleRunMode.InProcess,
            Changelog = entry?.Changelog,
        };
    }

    private ModuleManifest? ReadManifest(string id, string version)
    {
        lock (_sync)
        {
            if (_manifests.TryGetValue((id, version), out var cached)) return cached;
        }

        try
        {
            var manifest = installer.ReadInstalledManifest(id, version);
            if (manifest is not null)
            {
                lock (_sync) _manifests[(id, version)] = manifest;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            _log.Warn($"Не вдалося прочитати module.json модуля '{id}' {version}: {ex.Message}");
            return null;
        }
    }

    private void ForgetManifests(string id)
    {
        lock (_sync)
        {
            foreach (var key in _manifests.Keys.Where(k => k.Id == id).ToList()) _manifests.Remove(key);
        }
    }

    private InstalledModuleState? FindInstalled(string id)
    {
        lock (_sync) return state.State.Modules.GetValueOrDefault(id);
    }

    private ModuleRuntimeState? FindRuntime(string id)
    {
        lock (_sync) return _runtime.GetValueOrDefault(id);
    }

    private ModuleRuntimeState GetRuntime(string id)
    {
        lock (_sync)
        {
            if (!_runtime.TryGetValue(id, out var runtime)) _runtime[id] = runtime = new ModuleRuntimeState();
            return runtime;
        }
    }

    // Змінює стан під замком і записує його на диск.
    private void Commit(Action change)
    {
        lock (_sync) change();
        state.Save();
    }

    private void RaiseChanged()
    {
        EventHandler? handler;
        lock (_sync) handler = _changed;
        handler?.Invoke(this, EventArgs.Empty);
    }

    private static string ShortReason(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } e } ? e : exception;
        var text = inner.Message.Split('\n')[0].Trim();
        if (text.Length == 0) text = inner.GetType().Name;
        return text.Length > MaxReasonLength ? text[..MaxReasonLength] + "…" : text;
    }
}
