namespace Kastom.Core.Tests.Runtime;

/// <summary>
/// Видалення лаунчера (Velopack) стирає %LocalAppData%\Kastom разом із модулями, а стан у %AppData% лишається.
/// Після перевстановлення модулі мають повернутися з каталогу, а не падати й вимикатися.
/// </summary>
public sealed class ModuleRepairTests : IDisposable
{
    private readonly ModuleManagerFixture _f = new();

    public void Dispose() => _f.Dispose();

    private void CatalogWith(string id, string version) =>
        _f.Catalog.Current = FakeCatalogService.Catalog(FakeCatalogService.Entry(id, "Демо-модуль", version));

    [Fact]
    public async Task MissingEnabledModule_InCatalog_IsReinstalledAndStarted()
    {
        _f.Register("demo", "1.0.0", enabled: true);
        CatalogWith("demo", "1.0.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.Equal(["demo@1.0.0"], _f.Installer.Installed);
        var info = _f.Info("demo");
        Assert.False(info.IsMissing);
        Assert.False(info.IsRepairing);
        Assert.True(info.IsEnabled);
        Assert.True(info.IsRunning);
        Assert.Null(info.Error);
        Assert.True(_f.Store.State.Modules["demo"].Enabled);
        Assert.Contains(_f.Log.Lines, l => l.Contains("відновлено з каталогу"));
    }

    [Fact]
    public async Task MissingDisabledModule_IsReinstalledButStaysDisabled()
    {
        _f.Register("demo", "1.0.0", enabled: false, previousVersion: "0.9.0");
        CatalogWith("demo", "1.0.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.False(info.IsMissing);
        Assert.False(info.IsEnabled);
        Assert.False(info.IsRunning);
        Assert.Equal("1.0.0", info.InstalledVersion);
        // Файлів попередньої версії теж немає: відкат на неї неможливий.
        Assert.Null(info.PreviousVersion);
    }

    [Fact]
    public async Task MissingModule_CatalogHasNewerVersion_InstallsNewer()
    {
        _f.Register("demo", "1.0.0", enabled: true);
        CatalogWith("demo", "1.1.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.Equal(["demo@1.1.0"], _f.Installer.Installed);
        Assert.Equal("1.1.0", _f.Info("demo").InstalledVersion);
        Assert.True(_f.Info("demo").IsRunning);
    }

    [Fact]
    public async Task MissingModule_NotInCatalog_IsRemovedFromState()
    {
        _f.Register("gone", "1.0.0", enabled: true);
        CatalogWith("demo", "1.0.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.False(_f.Store.State.Modules.ContainsKey("gone"));
        Assert.DoesNotContain(_f.Manager.Modules, m => m.Id == "gone");
        Assert.Empty(_f.Installer.Installed);
        Assert.Contains(_f.Log.Lines, l => l.Contains("'gone'") && l.Contains("прибрано"));
    }

    [Fact]
    public async Task MissingModule_WithoutCatalog_StaysEnabledAndCanBeReinstalledLater()
    {
        _f.Register("demo", "1.0.0", enabled: true);
        _f.Catalog.Current = null;

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.True(info.IsMissing);
        Assert.True(info.IsEnabled);
        Assert.False(info.IsRunning);
        Assert.Equal("каталог недоступний", info.Error);
        Assert.True(_f.Store.State.Modules["demo"].Enabled);
        Assert.True(_f.Catalog.RefreshCalls >= 1);

        // Увімкнути без файлів не можна, і прапорець при цьому не змінюється.
        var enable = await _f.Manager.EnableAsync("demo");
        Assert.False(enable.Success);

        // Мережа з'явилась: «Перевстановити» ставить модуль і запускає його.
        CatalogWith("demo", "1.0.0");
        var reinstall = await _f.Manager.InstallAsync("demo");

        Assert.True(reinstall.Success, reinstall.Error);
        info = _f.Info("demo");
        Assert.False(info.IsMissing);
        Assert.True(info.IsRunning);
        Assert.Null(info.Error);
    }

    [Fact]
    public async Task MissingModule_CatalogHasOnlyOlderVersion_StaysMissing()
    {
        _f.Register("demo", "1.2.0", enabled: true);
        CatalogWith("demo", "1.0.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.True(info.IsMissing);
        Assert.True(info.IsEnabled);
        Assert.Contains("1.0.0", info.Error);
        Assert.Empty(_f.Installer.Installed);
    }

    [Fact]
    public async Task PresentModule_IsNotReinstalled()
    {
        _f.AddDemo();
        CatalogWith("demo", "1.0.0");

        await _f.Manager.StartAsync(CancellationToken.None);

        Assert.Empty(_f.Installer.Installed);
        Assert.False(_f.Info("demo").IsMissing);
        Assert.True(_f.Info("demo").IsRunning);
    }
}
