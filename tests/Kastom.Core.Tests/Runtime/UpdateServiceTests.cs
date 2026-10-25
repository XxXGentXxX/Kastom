using System.IO;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Core.Runtime;
using Kastom.Core.Updates;

namespace Kastom.Core.Tests.Runtime;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FakeCatalogService _catalog = new();
    private readonly FakeModuleManager _modules = new();
    private readonly FakeLauncherUpdater _launcher = new();
    private readonly ListLogger _log = new();
    private readonly JsonLauncherStateStore _state;
    private readonly UpdateService _service;

    public UpdateServiceTests()
    {
        _state = new JsonLauncherStateStore(_temp.Paths);
        _service = new UpdateService(_catalog, _modules, _state, _launcher, _log, TimeSpan.FromHours(1));
        _modules.ModuleList.Add(new ModuleInfo { Id = "audio", Name = "Звук", HasUpdate = true });
        _modules.ModuleList.Add(new ModuleInfo { Id = "demo", Name = "Демо", HasUpdate = false });
        _modules.ModuleList.Add(new ModuleInfo { Id = "tray", Name = "Трей", HasUpdate = true });
    }

    public void Dispose()
    {
        _service.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task CheckNow_FillsModulesWithUpdates_AndLastCheck()
    {
        var changes = 0;
        _service.StateChanged += (_, _) => changes++;

        await _service.CheckNowAsync();

        Assert.Equal(1, _catalog.RefreshCalls);
        Assert.Equal(["audio", "tray"], _service.ModulesWithUpdates);
        Assert.NotNull(_service.LastCheck);
        Assert.False(_service.IsChecking);
        Assert.True(changes >= 2);
        Assert.Empty(_modules.Updated);
    }

    [Fact]
    public async Task CheckNow_ReportsLauncherVersion_OnlyWhenInstalled()
    {
        _launcher.NewVersion = "0.2.0";

        await _service.CheckNowAsync();
        Assert.Null(_service.LauncherAvailableVersion);

        _launcher.IsInstalled = true;
        await _service.CheckNowAsync();
        Assert.Equal("0.2.0", _service.LauncherAvailableVersion);
    }

    [Fact]
    public async Task NotifyMode_DoesNotUpdateModules()
    {
        _state.State.UpdateMode = UpdateMode.Notify;

        await _service.CheckNowAsync();

        Assert.Empty(_modules.Updated);
    }

    [Fact]
    public async Task AutomaticMode_UpdatesModules_ButNotLauncher()
    {
        _state.State.UpdateMode = UpdateMode.Automatic;
        _launcher.IsInstalled = true;
        _launcher.NewVersion = "0.2.0";

        await _service.CheckNowAsync();

        Assert.Equal(["audio", "tray"], _modules.Updated);
        Assert.Equal(0, _launcher.ApplyCalls);
        Assert.Equal("0.2.0", _service.LauncherAvailableVersion);
    }

    [Fact]
    public async Task AutomaticMode_RaisesStateChanged_AfterModulesWereUpdated()
    {
        _state.State.UpdateMode = UpdateMode.Automatic;
        var sequence = new List<string>();
        _modules.OnUpdate = id => sequence.Add("update:" + id);
        _service.StateChanged += (_, _) => sequence.Add("changed");

        await _service.CheckNowAsync();

        var lastUpdate = sequence.FindLastIndex(e => e.StartsWith("update:"));
        Assert.True(lastUpdate >= 0);
        Assert.Contains("changed", sequence.Skip(lastUpdate + 1));
    }

    [Fact]
    public async Task CheckNow_SwallowsExceptions_AndResetsIsChecking()
    {
        _catalog.OnRefresh = () => throw new InvalidOperationException("мережа");

        await _service.CheckNowAsync();

        Assert.False(_service.IsChecking);
        Assert.Contains(_log.Lines, l => l.StartsWith("[ERROR]"));
    }

    [Fact]
    public async Task CheckNow_WhileChecking_IsSkipped()
    {
        var gate = new TaskCompletionSource();
        _catalog.OnRefresh = () => gate.Task;

        var first = _service.CheckNowAsync();
        Assert.True(_service.IsChecking);
        await _service.CheckNowAsync();
        gate.SetResult();
        await first;

        Assert.Equal(1, _catalog.RefreshCalls);
        Assert.False(_service.IsChecking);
    }

    [Fact]
    public async Task ApplyLauncherUpdate_CallsUpdater()
    {
        await _service.ApplyLauncherUpdateAsync();

        Assert.Equal(1, _launcher.ApplyCalls);
    }

    [Fact]
    public async Task Start_RunsFirstCheckAfterInitialDelay_AndDisposeStopsLoop()
    {
        var checked_ = new TaskCompletionSource();
        _catalog.OnRefresh = () =>
        {
            checked_.TrySetResult();
            return Task.CompletedTask;
        };
        _service.InitialDelay = TimeSpan.FromMilliseconds(50);

        _service.Start();
        _service.Start();
        await checked_.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _service.Dispose();
        await Task.Delay(150);

        Assert.Equal(1, _catalog.RefreshCalls);
    }
}
