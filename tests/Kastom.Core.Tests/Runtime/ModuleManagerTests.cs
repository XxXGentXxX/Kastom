using System.IO;
using Kastom.Core.Installation;
using Kastom.Core.Models;
using Kastom.Core.Runtime;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Settings;

namespace Kastom.Core.Tests.Runtime;

public sealed class ModuleManagerTests : IDisposable
{
    private readonly ModuleManagerFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void DemoBuildOutput_HasManifest_AndNoSdkCopy()
    {
        Assert.True(File.Exists(Path.Combine(ModuleManagerFixture.DemoOutput, "module.json")));
        Assert.True(File.Exists(Path.Combine(ModuleManagerFixture.DemoOutput, "Kastom.Modules.Demo.dll")));
        Assert.False(File.Exists(Path.Combine(ModuleManagerFixture.DemoOutput, "Kastom.Sdk.dll")));
    }

    [Fact]
    public async Task Start_LoadsConfiguresAndStartsDemo()
    {
        _f.AddDemo();

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.True(info.IsRunning);
        Assert.True(info.IsEnabled);
        Assert.Null(info.Error);
        Assert.Equal("Демо-модуль", info.Name);
        Assert.Equal("demo/hello", _f.Actions.Find("demo/hello")!.GlobalId);
        Assert.NotNull(_f.Actions.Find("demo/count"));
        Assert.Contains(_f.Log.Lines, l => l.Contains("Демо-модуль запущено."));

        var settings = _f.Manager.GetSettings("demo")!;
        Assert.Equal(["show-greeting", "intensity", "accent", "say-hello"], settings.Definitions.Select(d => d.Key));
        Assert.True(settings.GetBool("show-greeting"));
        Assert.Equal(50, settings.GetDouble("intensity"));
        Assert.Equal("blue", settings.GetString("accent"));
        Assert.IsType<RangeSetting>(settings.Definitions[1]);
        var widget = Assert.Single(_f.Manager.GetWidgets("demo"));
        Assert.Equal("counter", widget.Id);
        Assert.Equal((220.0, 120.0), (widget.DefaultWidth, widget.DefaultHeight));
        Assert.True(Directory.Exists(_f.Paths.ModuleDataDirectory("demo")));
    }

    [Fact]
    public async Task DemoActions_WorkThroughActionCatalog()
    {
        _f.AddDemo();
        await _f.Manager.StartAsync(CancellationToken.None);

        await _f.Actions.ExecuteAsync("demo/hello", ActionTrigger.Hotkey);
        Assert.Equal(["Привіт від демо-модуля!"], _f.Notifications.Messages);

        _f.Manager.GetSettings("demo")!.Set("show-greeting", false);
        await _f.Actions.ExecuteAsync("demo/hello", ActionTrigger.Ui);
        await _f.Actions.ExecuteAsync("demo/count", ActionTrigger.Ui);

        Assert.Single(_f.Notifications.Messages);
        Assert.DoesNotContain(_f.Log.Lines, l => l.StartsWith("[ERROR]"));
        Assert.NotNull(_f.Actions.Find("demo/hello")!.Action.DefaultHotkey);
    }

    [Fact]
    public async Task DisableThenEnable_StopsAndRemovesActions_WithoutSecondConfigure()
    {
        _f.AddDemo();
        await _f.Manager.StartAsync(CancellationToken.None);
        var settings = _f.Manager.GetSettings("demo");

        var disabled = await _f.Manager.DisableAsync("demo");

        Assert.True(disabled.Success);
        Assert.False(_f.Info("demo").IsRunning);
        Assert.False(_f.Info("demo").IsEnabled);
        Assert.Empty(_f.Actions.All);
        Assert.Contains(_f.Log.Lines, l => l.Contains("Демо-модуль зупинено."));
        Assert.False(new JsonLauncherStateStore(_f.Paths).State.Modules["demo"].Enabled);
        Assert.Same(settings, _f.Manager.GetSettings("demo"));

        var enabled = await _f.Manager.EnableAsync("demo");

        Assert.True(enabled.Success);
        Assert.False(enabled.RestartRequired);
        Assert.True(_f.Info("demo").IsRunning);
        Assert.Equal(2, _f.Actions.All.Count);
        Assert.Same(settings, _f.Manager.GetSettings("demo"));
        Assert.Equal(4, settings!.Definitions.Count);
    }

    [Fact]
    public async Task Start_ModuleWithThrowingConfigure_IsDisabled_OthersStillStart()
    {
        _f.AddTestAssemblyModule("broken", "Зламаний", typeof(ThrowingModule));
        _f.AddDemo();

        await _f.Manager.StartAsync(CancellationToken.None);

        var broken = _f.Info("broken");
        Assert.False(broken.IsRunning);
        Assert.False(broken.IsEnabled);
        Assert.Contains("Configure зламався", broken.Error);
        Assert.True(_f.Info("demo").IsRunning);
        Assert.False(new JsonLauncherStateStore(_f.Paths).State.Modules["broken"].Enabled);
        Assert.True(new JsonLauncherStateStore(_f.Paths).State.Modules["demo"].Enabled);
        Assert.Contains(_f.Notifications.Messages, m => m.StartsWith("Модуль «Зламаний» вимкнено: ") && m.Contains("Configure зламався"));
        Assert.DoesNotContain(_f.Actions.All, a => a.ModuleId == "broken");
        Assert.Null(_f.Manager.GetSettings("broken"));
    }

    [Fact]
    public async Task Start_ModuleThatHangs_TimesOut()
    {
        _f.Manager.ModuleTimeout = TimeSpan.FromMilliseconds(300);
        _f.AddTestAssemblyModule("hang", "Завислий", typeof(HangingModule));

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("hang");
        Assert.False(info.IsRunning);
        Assert.False(info.IsEnabled);
        Assert.Contains("перевищено", info.Error);
    }

    [Fact]
    public async Task Start_OutOfProcessModule_IsRejected()
    {
        _f.AddDemo(runMode: "outOfProcess");

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.False(info.IsRunning);
        Assert.False(info.IsEnabled);
        Assert.Equal("Модулі в окремому процесі ще не підтримуються", info.Error);
        Assert.Contains("Модуль «Демо-модуль» вимкнено: Модулі в окремому процесі ще не підтримуються", _f.Notifications.Messages);
    }

    [Fact]
    public async Task Start_MissingManifest_DisablesModule()
    {
        _f.Register("ghost", "1.0.0", enabled: true);

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("ghost");
        Assert.False(info.IsEnabled);
        Assert.NotNull(info.Error);
        Assert.Equal("ghost", info.Name);
    }

    [Fact]
    public async Task Start_IncompatibleManifest_DisablesModule()
    {
        _f.AddDemo();
        _f.Manager.CompatibilityCheck = (_, _, _) => new CompatibilityResult(false, "Потрібен новіший лаунчер.");

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.Equal("Потрібен новіший лаунчер.", _f.Info("demo").Error);
        Assert.False(_f.Info("demo").IsRunning);
    }

    [Fact]
    public async Task Start_RemovesStaleVersionFolders_KeepsCurrentAndPrevious()
    {
        _f.AddDemo("1.0.0", enabled: false, previousVersion: "0.9.0");
        Directory.CreateDirectory(_f.Paths.ModuleInstallDirectory("demo", "0.9.0"));
        Directory.CreateDirectory(_f.Paths.ModuleInstallDirectory("demo", "0.8.0"));

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.Equal([("demo", "0.8.0")], _f.Installer.RemovedVersions);
        Assert.True(Directory.Exists(_f.Paths.ModuleInstallDirectory("demo", "0.9.0")));
        Assert.True(Directory.Exists(_f.Paths.ModuleInstallDirectory("demo", "1.0.0")));
        Assert.False(_f.Info("demo").IsRunning);
    }

    [Fact]
    public async Task UninstallLoadedModule_GoesToPendingRemovals_AndIsRemovedOnNextStart()
    {
        _f.AddDemo();
        await _f.Manager.StartAsync(CancellationToken.None);

        var result = await _f.Manager.UninstallAsync("demo");

        Assert.True(result.Success);
        Assert.True(result.RestartRequired);
        Assert.Empty(_f.Actions.All);
        Assert.Empty(_f.Installer.RemovedModules);
        Assert.True(Directory.Exists(_f.Paths.ModuleInstallDirectory("demo", "1.0.0")));
        var saved = new JsonLauncherStateStore(_f.Paths).State;
        Assert.DoesNotContain("demo", saved.Modules.Keys);
        Assert.Equal(["demo"], saved.PendingRemovals);

        var nextLaunch = _f.CreateManager(new JsonLauncherStateStore(_f.Paths));
        await nextLaunch.StartAsync(CancellationToken.None);

        Assert.Equal(["demo"], _f.Installer.RemovedModules);
        Assert.Empty(new JsonLauncherStateStore(_f.Paths).State.PendingRemovals);
    }

    [Fact]
    public async Task UninstallModuleThatWasNeverLoaded_RemovesFilesImmediately()
    {
        _f.AddDemo(enabled: false);

        var result = await _f.Manager.UninstallAsync("demo");

        Assert.True(result.Success);
        Assert.False(result.RestartRequired);
        Assert.Equal(["demo"], _f.Installer.RemovedModules);
        Assert.Empty(_f.Store.State.PendingRemovals);
        Assert.Empty(_f.Manager.Modules);
    }

    [Fact]
    public async Task InstallUpdateRollback_FollowRestartRules()
    {
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.0.0"));

        var installed = await _f.Manager.InstallAsync("demo");

        Assert.True(installed.Success);
        Assert.False(installed.RestartRequired);
        Assert.True(_f.Info("demo").IsRunning);
        Assert.Equal("1.0.0", _f.Store.State.Modules["demo"].Version);

        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.1.0"));
        Assert.True(_f.Info("demo").HasUpdate);

        var updated = await _f.Manager.UpdateAsync("demo");

        Assert.True(updated.Success);
        Assert.True(updated.RestartRequired);
        var info = _f.Info("demo");
        Assert.True(info.RestartRequired);
        Assert.True(info.IsRunning);
        Assert.False(info.HasUpdate);
        Assert.Equal("1.1.0", info.InstalledVersion);
        Assert.Equal("1.0.0", info.PreviousVersion);
        Assert.Equal("1.1.0", new JsonLauncherStateStore(_f.Paths).State.Modules["demo"].Version);

        var rolledBack = await _f.Manager.RollbackAsync("demo");

        Assert.True(rolledBack.Success);
        Assert.False(rolledBack.RestartRequired);
        Assert.Equal("1.0.0", _f.Info("demo").InstalledVersion);
        Assert.Equal("1.1.0", _f.Info("demo").PreviousVersion);
        Assert.False(_f.Info("demo").RestartRequired);
    }

    [Fact]
    public async Task EnableModuleWhoseLoadedVersionDiffers_AsksForRestart()
    {
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.0.0"));
        await _f.Manager.InstallAsync("demo");
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.1.0"));
        await _f.Manager.UpdateAsync("demo");
        await _f.Manager.DisableAsync("demo");

        var enabled = await _f.Manager.EnableAsync("demo");

        Assert.True(enabled.Success);
        Assert.True(enabled.RestartRequired);
        Assert.False(_f.Info("demo").IsRunning);
        Assert.True(_f.Info("demo").RestartRequired);
    }

    [Fact]
    public async Task Install_IncompatibleEntry_FailsWithoutCallingInstaller()
    {
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.0.0"));
        _f.Manager.CompatibilityCheck = (_, _, _) => new CompatibilityResult(false, "Потрібен новіший лаунчер.");

        var result = await _f.Manager.InstallAsync("demo");

        Assert.False(result.Success);
        Assert.Equal("Потрібен новіший лаунчер.", result.Error);
        Assert.Empty(_f.Installer.Installed);
        Assert.False(_f.Info("demo").IsInstalled);
    }

    [Fact]
    public async Task Install_UnknownModule_Fails_AndUpdateOfNotInstalledFails()
    {
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry("demo", "Демо-модуль", "1.0.0"));

        var unknown = await _f.Manager.InstallAsync("nope");
        var notInstalled = await _f.Manager.UpdateAsync("demo");

        Assert.False(unknown.Success);
        Assert.False(notInstalled.Success);
        Assert.Empty(_f.Installer.Installed);
    }

    [Fact]
    public void Modules_UnionOfInstalledAndCatalog_SortedByName()
    {
        _f.AddDemo();
        _f.Catalog.Current = FakeCatalogService.Catalog(
            FakeCatalogService.Entry("demo", "Інша назва", "1.1.0"),
            new CatalogEntry
            {
                Id = "alpha",
                Name = "Альфа",
                Description = "Опис",
                Version = "2.0.0",
                ContractVersion = 1,
                MinLauncherVersion = "9.9.9",
                Url = "https://example.invalid/alpha.zip",
                Sha256 = new string('0', 64),
                Changelog = "Нове",
            });
        _f.Manager.CompatibilityCheck = (_, minimum, _) => new CompatibilityResult(minimum != "9.9.9", "Старий лаунчер");

        var modules = _f.Manager.Modules;

        Assert.Equal(["alpha", "demo"], modules.Select(m => m.Id));
        var alpha = modules[0];
        Assert.False(alpha.IsInstalled);
        Assert.False(alpha.IsCompatible);
        Assert.False(alpha.HasUpdate);
        Assert.Equal("Опис", alpha.Description);
        Assert.Equal("Нове", alpha.Changelog);
        var demo = modules[1];
        Assert.Equal("Демо-модуль", demo.Name);
        Assert.Equal("1.0.0", demo.InstalledVersion);
        Assert.Equal("1.1.0", demo.AvailableVersion);
        Assert.True(demo.HasUpdate);
        Assert.True(demo.IsCompatible);
    }

    [Fact]
    public async Task StopAll_StopsRunningModules()
    {
        _f.AddDemo();
        await _f.Manager.StartAsync(CancellationToken.None);

        await _f.Manager.StopAllAsync(CancellationToken.None);

        Assert.False(_f.Info("demo").IsRunning);
        Assert.Empty(_f.Actions.All);
        Assert.True(_f.Store.State.Modules["demo"].Enabled);
    }

    [Fact]
    public async Task Changed_IsRaised_AfterOperations()
    {
        _f.AddDemo();
        var changed = 0;
        _f.Manager.Changed += (_, _) => changed++;

        await _f.Manager.StartAsync(CancellationToken.None);
        await _f.Manager.DisableAsync("demo");
        _f.Catalog.RaiseChanged();

        Assert.Equal(3, changed);
    }
}
