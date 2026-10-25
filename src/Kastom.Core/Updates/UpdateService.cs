using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Sdk.Services;

namespace Kastom.Core.Updates;

public sealed class UpdateService(
    ICatalogService catalog,
    IModuleManager modules,
    ILauncherStateStore state,
    ILauncherUpdater launcherUpdater,
    IModuleLogger log,
    TimeSpan interval) : IUpdateService
{
    private readonly CancellationTokenSource _stop = new();
    private int _checking;
    private int _started;
    private int _disposed;
    private readonly object _lastCheckLock = new();
    private DateTimeOffset? _lastCheck;
    private string? _launcherAvailableVersion;
    private IReadOnlyList<string> _modulesWithUpdates = [];

    /// <summary>Пауза перед першою перевіркою після Start; у тестах скорочується.</summary>
    internal TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    public DateTimeOffset? LastCheck
    {
        get
        {
            lock (_lastCheckLock) return _lastCheck;
        }
    }

    public bool IsChecking => Volatile.Read(ref _checking) != 0;

    public string? LauncherAvailableVersion => Volatile.Read(ref _launcherAvailableVersion);

    public IReadOnlyList<string> ModulesWithUpdates => Volatile.Read(ref _modulesWithUpdates);

    public event EventHandler? StateChanged;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = RunLoopAsync(_stop.Token);
    }

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;
        RaiseStateChanged();
        try
        {
            await CheckCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.Error("Перевірка оновлень завершилась помилкою.", ex);
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
            RaiseStateChanged();
        }
    }

    public Task ApplyLauncherUpdateAsync(CancellationToken cancellationToken = default) =>
        launcherUpdater.DownloadAndRestartAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialDelay, cancellationToken);
            await CheckNowAsync(cancellationToken);

            if (interval <= TimeSpan.Zero) return;
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken)) await CheckNowAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Цикл перевірки оновлень зупинився.", ex);
        }
    }

    private async Task CheckCoreAsync(CancellationToken cancellationToken)
    {
        var fetch = await catalog.RefreshAsync(cancellationToken);
        if (fetch.Status is CatalogFetchStatus.Failed or CatalogFetchStatus.InvalidSignature)
            log.Warn($"Каталог не оновлено ({fetch.Status}): {fetch.Error}");

        var withUpdates = FindModulesWithUpdates();
        Volatile.Write(ref _modulesWithUpdates, withUpdates);

        if (launcherUpdater.IsInstalled)
        {
            try
            {
                Volatile.Write(ref _launcherAvailableVersion, await launcherUpdater.CheckForUpdateAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Error("Не вдалося перевірити оновлення лаунчера.", ex);
            }
        }

        // Оновлення лаунчера лише оголошуємо; модулі ставимо самі тільки в автоматичному режимі.
        if (state.State.UpdateMode == UpdateMode.Automatic)
        {
            foreach (var id in withUpdates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await modules.UpdateAsync(id, null, cancellationToken);
                if (!result.Success) log.Warn($"Не вдалося оновити модуль '{id}': {result.Error}");
            }

            Volatile.Write(ref _modulesWithUpdates, FindModulesWithUpdates());

            // Нові версії вже на диску, а працюють старі: оболонка може сказати про перезапуск.
            if (withUpdates.Length > 0) RaiseStateChanged();
        }

        lock (_lastCheckLock) _lastCheck = DateTimeOffset.Now;
    }

    private string[] FindModulesWithUpdates() => modules.Modules.Where(m => m.HasUpdate).Select(m => m.Id).ToArray();

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
