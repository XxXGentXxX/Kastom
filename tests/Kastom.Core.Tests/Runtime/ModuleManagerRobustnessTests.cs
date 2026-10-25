using System.Diagnostics;
using System.IO;
using Kastom.Core.Runtime;
using Kastom.Sdk.Settings;

namespace Kastom.Core.Tests.Runtime;

[Collection(TimingSensitiveCollection.Name)]
public sealed class ModuleManagerRobustnessTests : IDisposable
{
    private readonly ModuleManagerFixture _f = new();

    public void Dispose() => _f.Dispose();

    private void CatalogWith(string id, string version) =>
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry(id, "Демо-модуль", version));

    [Fact]
    public async Task Update_OfDisabledModule_KeepsItDisabledAndNotLoaded()
    {
        _f.AddDemo("1.0.0", enabled: false);
        CatalogWith("demo", "1.1.0");

        var result = await _f.Manager.UpdateAsync("demo");

        Assert.True(result.Success);
        Assert.False(result.RestartRequired);
        var info = _f.Info("demo");
        Assert.Equal("1.1.0", info.InstalledVersion);
        Assert.Equal("1.0.0", info.PreviousVersion);
        Assert.False(info.IsEnabled);
        Assert.False(info.IsRunning);
        Assert.Null(_f.Manager.GetSettings("demo"));
        Assert.False(new JsonLauncherStateStore(_f.Paths).State.Modules["demo"].Enabled);
        Assert.Empty(_f.Actions.All);
    }

    [Fact]
    public async Task Rollback_OfDisabledModule_KeepsItDisabledAndNotLoaded()
    {
        _f.AddDemo("1.0.0", enabled: false);
        CatalogWith("demo", "1.1.0");
        await _f.Manager.UpdateAsync("demo");

        var result = await _f.Manager.RollbackAsync("demo");

        Assert.True(result.Success);
        Assert.Equal("1.0.0", _f.Info("demo").InstalledVersion);
        Assert.False(_f.Info("demo").IsEnabled);
        Assert.False(_f.Info("demo").IsRunning);
        Assert.Null(_f.Manager.GetSettings("demo"));
    }

    [Fact]
    public async Task FreshInstall_EnablesModule_ReinstallOfDisabledDoesNot()
    {
        CatalogWith("demo", "1.0.0");
        await _f.Manager.InstallAsync("demo");
        Assert.True(_f.Info("demo").IsEnabled);

        await _f.Manager.DisableAsync("demo");
        await _f.Manager.InstallAsync("demo");

        Assert.False(_f.Info("demo").IsEnabled);
        Assert.False(_f.Info("demo").IsRunning);
    }

    [Fact]
    public async Task Start_ModuleThatBlocksSynchronously_TimesOutWithoutFreezingCaller()
    {
        _f.Manager.ModuleTimeout = TimeSpan.FromMilliseconds(300);
        _f.AddTestAssemblyModule("block", "Блокуючий", typeof(BlockingStartModule));

        var watch = Stopwatch.StartNew();
        var start = _f.Manager.StartAsync(CancellationToken.None);
        var returnedControl = watch.Elapsed;
        await start;
        watch.Stop();

        // Виклик не блокує потік на 2,5 с, які спить модуль, а повертається за таймаутом.
        Assert.True(returnedControl < TimeSpan.FromMilliseconds(1500), $"StartAsync повернув керування за {returnedControl}");
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(1800), $"Старт тривав {watch.Elapsed}");
        var info = _f.Info("block");
        Assert.Contains("перевищено", info.Error);
        Assert.False(info.IsEnabled);
        Assert.False(info.IsRunning);
    }

    [Fact]
    public async Task Start_CancelledByLauncherExit_ModuleIsStillAskedToStop()
    {
        SlowStartModule.Reset();
        _f.Manager.ModuleTimeout = TimeSpan.FromSeconds(5);
        _f.AddTestAssemblyModule("slow", "Повільний", typeof(SlowStartModule));

        var start = _f.Manager.StartAsync(CancellationToken.None);
        await WaitUntil(() => SlowStartModule.Started);

        // Вихід лаунчера посеред запуску: модуль міг устигнути щось змінити, тож його мусять попросити зупинитися.
        await _f.Manager.StopAllAsync(CancellationToken.None);
        try
        {
            await start;
        }
        catch (OperationCanceledException)
        {
            // Скасований запуск допустимий.
        }

        await WaitUntil(() => SlowStartModule.Stopped);
        Assert.True(SlowStartModule.Stopped, "StopAsync не викликали після перерваного запуску");
    }

    [Fact]
    public async Task StopAll_StopsModulesInParallel_WithOwnTimeouts()
    {
        string[] ids = ["stop-a", "stop-b", "stop-c"];
        _f.Manager.StopAllTimeout = TimeSpan.FromMilliseconds(600);
        foreach (var id in ids) _f.AddTestAssemblyModule(id, id, typeof(BlockingStopModule));
        await _f.Manager.StartAsync(CancellationToken.None);
        Assert.All(ids, id => Assert.True(_f.Info(id).IsRunning));

        var watch = Stopwatch.StartNew();
        await _f.Manager.StopAllAsync(CancellationToken.None);
        watch.Stop();

        // Послідовно вийшло б 3 x 0,6 с.
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(1500), $"Зупинка тривала {watch.Elapsed}");
        Assert.All(ids, id => Assert.False(_f.Info(id).IsRunning));
        Assert.Equal(3, _f.Log.Lines.Count(l => l.StartsWith("[ERROR]") && l.Contains("не зупинився чисто")));
        Assert.Empty(_f.Actions.All);
    }

    [Fact]
    public async Task StopAll_WhenGateIsBusyAndTokenCancelled_StillStopsModules()
    {
        _f.AddDemo();
        await _f.Manager.StartAsync(CancellationToken.None);
        CatalogWith("other", "1.0.0");
        var hold = new TaskCompletionSource();
        _f.Installer.OnInstall = () => hold.Task;
        var busy = _f.Manager.InstallAsync("other");
        await WaitUntil(() => _f.Installer.Installed.Count == 1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await _f.Manager.StopAllAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(_f.Info("demo").IsRunning);
        Assert.Empty(_f.Actions.All);
        Assert.Contains(_f.Log.Lines, l => l.StartsWith("[WARN]") && l.Contains("без неї"));

        hold.SetResult();
        await busy.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task OperationsWaitingForGate_AreCancelledOnShutdown()
    {
        _f.AddDemo();
        CatalogWith("other", "1.0.0");
        var hold = new TaskCompletionSource();
        _f.Installer.OnInstall = () => hold.Task;
        var busy = _f.Manager.InstallAsync("other");
        await WaitUntil(() => _f.Installer.Installed.Count == 1);

        var disable = _f.Manager.DisableAsync("demo");
        var uninstall = _f.Manager.UninstallAsync("demo");
        await Task.Delay(100);
        Assert.False(disable.IsCompleted);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await _f.Manager.StopAllAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        var disabled = await disable.WaitAsync(TimeSpan.FromSeconds(10));
        var uninstalled = await uninstall.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(disabled.Success);
        Assert.Equal("Скасовано.", disabled.Error);
        Assert.False(uninstalled.Success);
        Assert.True(_f.Store.State.Modules["demo"].Enabled);

        hold.SetResult();
        await busy.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UpdateWithToken_WaitingForGate_ReturnsCancelledWhenTokenFires()
    {
        CatalogWith("other", "1.0.0");
        var hold = new TaskCompletionSource();
        _f.Installer.OnInstall = () => hold.Task;
        var busy = _f.Manager.InstallAsync("other");
        await WaitUntil(() => _f.Installer.Installed.Count == 1);

        using var cts = new CancellationTokenSource();
        var waiting = _f.Manager.UpdateAsync("other", null, cts.Token);
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);
        cts.Cancel();

        var result = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Equal("Скасовано.", result.Error);
        Assert.Single(_f.Installer.Installed);

        hold.SetResult();
        await busy.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Умова не виконалась за 10 с.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Запуск триває ~0,8 с і не зважає на скасування; фіксує, що StopAsync таки викликали.
/// Модуль живе в окремому AssemblyLoadContext (копія тестової збірки), тож його статичні поля — інші, ніж у тесту;
/// сигнали передаються через дані AppDomain, спільні для всього процесу.
/// </summary>
public sealed class SlowStartModule : Kastom.Sdk.IKastomModule
{
    private const string StartedKey = "Kastom.Tests.SlowStart.Started";
    private const string StoppedKey = "Kastom.Tests.SlowStart.Stopped";

    public static bool Started => AppDomain.CurrentDomain.GetData(StartedKey) is true;

    public static bool Stopped => AppDomain.CurrentDomain.GetData(StoppedKey) is true;

    public static void Reset()
    {
        AppDomain.CurrentDomain.SetData(StartedKey, null);
        AppDomain.CurrentDomain.SetData(StoppedKey, null);
    }

    public void Configure(Kastom.Sdk.IModuleContext context)
    {
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.SetData(StartedKey, true);
        await Task.Delay(800, CancellationToken.None);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.SetData(StoppedKey, true);
        return Task.CompletedTask;
    }
}

public sealed class AtomicWriteTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WriteAllText_ReplacesFile_AndLeavesNoTemp()
    {
        var file = Path.Combine(_temp.FullPath, "sub", "a.json");

        AtomicFile.WriteAllText(file, "один");
        AtomicFile.WriteAllText(file, "два");

        Assert.Equal("два", File.ReadAllText(file));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void WriteAllText_WhenWriteFails_KeepsOldFile()
    {
        var file = Path.Combine(_temp.FullPath, "a.json");
        File.WriteAllText(file, "старе");
        Directory.CreateDirectory(file + ".tmp");

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(file, "нове"));

        Assert.Equal("старе", File.ReadAllText(file));
    }

    [Fact]
    public void SettingsSet_WhenWriteFails_KeepsOldValue_AndDoesNotRaiseChanged()
    {
        var paths = _temp.Paths;
        var registry = new ModuleSettingsRegistry(paths, "demo");
        registry.Define(new ToggleSetting { Key = "flag", Title = "t", Default = false });
        var raised = 0;
        registry.Changed += (_, _) => raised++;
        registry.Set("flag", true);

        // Місце тимчасового файлу зайнято папкою: запис неможливий.
        Directory.CreateDirectory(paths.ModuleSettingsFile("demo") + ".tmp");
        Assert.ThrowsAny<Exception>(() => registry.Set("flag", false));

        Assert.True(registry.GetBool("flag"));
        Assert.Equal(1, raised);

        var reloaded = new ModuleSettingsRegistry(paths, "demo");
        reloaded.Define(new ToggleSetting { Key = "flag", Title = "t", Default = false });
        Assert.True(reloaded.GetBool("flag"));
    }
}
