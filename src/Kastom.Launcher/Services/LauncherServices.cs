using System.Net.Http;
using System.Reflection;
using Kastom.Core;
using Kastom.Core.Abstractions;
using Kastom.Core.Catalog;
using Kastom.Core.Hotkeys;
using Kastom.Core.Installation;
using Kastom.Core.Logging;
using Kastom.Core.Runtime;
using Kastom.Core.Updates;
using Kastom.Launcher.Hotkeys;
using Kastom.Launcher.Notifications;
using Kastom.Launcher.Pages;
using Kastom.Launcher.Widgets;
using Kastom.Sdk.Services;

namespace Kastom.Launcher.Services;

/// <summary>Усі служби лаунчера, зібрані в одному місці. Створюється після App, в потоці інтерфейсу.</summary>
public sealed class LauncherServices : IDisposable
{
    public LauncherServices(
        LaunchOptions options,
        KastomPaths paths,
        Func<Task> shutdownForUpdate,
        Action<string?> openMainWindow)
    {
        Options = options;
        Paths = paths;
        LauncherVersion = ReadLauncherVersion();
        Log = new SafeLogger(new FileLogger(paths, "launcher"));

        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Http.DefaultRequestHeaders.UserAgent.TryParseAdd($"Kastom/{LauncherVersion}");

        StateStore = new JsonLauncherStateStore(paths);
        Ui = new WpfUiDispatcher();
        Screens = new ScreenPolicy(options.TestScreen);
        Notifications = new OsdNotificationService(Screens);
        Autostart = new AutostartService(useRegistry: options.DataDirectory is null);

        var verifier = new CatalogVerifier(KastomTrust.CatalogPublicKey);
        ICatalogSource source;
        if (options.CatalogDirectory is { } catalogDirectory)
        {
            source = new FileCatalogSource(catalogDirectory, verifier);
            CatalogDescription = catalogDirectory;
        }
        else
        {
            var catalogUrl = ResolveCatalogUrl();
            source = new HttpCatalogSource(Http, catalogUrl, verifier);
            CatalogDescription = catalogUrl.ToString();
        }

        Catalog = new ObservingCatalogService(new CatalogService(source, verifier, paths, Log));
        Installer = new ModuleInstaller(Http, paths, Log, LauncherVersion, source.AllowsFileUrls);
        Actions = new ActionCatalog(Ui, Log);
        Modules = new ModuleManager(
            paths, StateStore, Catalog, Installer, Actions, Notifications, Ui,
            id => new SafeLogger(new FileLogger(paths, id)), LauncherVersion);
        // Пісочниця (--data-dir) може працювати з exe встановленого лаунчера: оновлення зачепило б справжню копію.
        Updater = new VelopackLauncherUpdater(Catalog, LauncherVersion, Log, shutdownForUpdate, selfUpdateAllowed: options.DataDirectory is null);
        Updates = new UpdateService(Catalog, Modules, StateStore, Updater, Log, TimeSpan.FromHours(1));
        Widgets = new WidgetHost(StateStore, Modules, Screens, Log, () => new ModulesQuickView(Modules, Actions, openMainWindow));
    }

    public LaunchOptions Options { get; }

    /// <summary>Скасовується першим при виході: обриває встановлення й оновлення модулів, що тривають.</summary>
    public CancellationTokenSource OperationsCts { get; } = new();

    public KastomPaths Paths { get; }

    public ModuleVersion LauncherVersion { get; }

    public IModuleLogger Log { get; }

    public HttpClient Http { get; }

    public ILauncherStateStore StateStore { get; }

    public IUiDispatcher Ui { get; }

    public INotificationService Notifications { get; }

    public AutostartService Autostart { get; }

    /// <summary>Де відкривати вікна й чи можна брати фокус (--test-screen).</summary>
    public ScreenPolicy Screens { get; }

    /// <summary>Адреса каталогу (або локальна папка розробника) для показу в налаштуваннях.</summary>
    public string CatalogDescription { get; }

    public ObservingCatalogService Catalog { get; }

    public IModuleInstaller Installer { get; }

    public ActionCatalog Actions { get; }

    public IModuleManager Modules { get; }

    public ILauncherUpdater Updater { get; }

    public IUpdateService Updates { get; }

    public WidgetHost Widgets { get; }

    public IHotkeyBackend? HotkeyBackend { get; private set; }

    public IHotkeyManager? Hotkeys { get; private set; }

    /// <summary>
    /// Створити менеджер клавіш. Викликати після реєстрації дій лаунчера, в потоці інтерфейсу.
    /// <paramref name="registerInSystem"/> = false — без реального RegisterHotKey (режим знімків).
    /// </summary>
    public void CreateHotkeys(bool registerInSystem)
    {
        HotkeyBackend = registerInSystem ? new Win32HotkeyBackend() : new NullHotkeyBackend();
        try
        {
            Hotkeys = new HotkeyManager(Actions, StateStore, HotkeyBackend, Log);
        }
        catch
        {
            HotkeyBackend.Dispose();
            HotkeyBackend = null;
            throw;
        }
    }

    /// <summary>Зупинити перевірку оновлень.</summary>
    public void DisposeUpdates() => TryDispose(Updates);

    /// <summary>Зняти гарячі клавіші.</summary>
    public void DisposeHotkeys()
    {
        TryDispose(Hotkeys);
        TryDispose(HotkeyBackend);
        Hotkeys = null;
        HotkeyBackend = null;
    }

    public void Dispose()
    {
        try { OperationsCts.Cancel(); }
        catch (ObjectDisposedException) { }
        DisposeUpdates();
        DisposeHotkeys();
        Http.Dispose();
        OperationsCts.Dispose();
    }

    private void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Error("Не вдалося звільнити службу", exception);
        }
    }

    private Uri ResolveCatalogUrl()
    {
        string? custom = null;
        try
        {
            custom = StateStore.State.CatalogUrlOverride;
        }
        catch (Exception exception)
        {
            Log.Error("Не вдалося прочитати адресу каталогу зі стану", exception);
        }

        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (Uri.TryCreate(custom, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri;
            Log.Warn($"Адреса каталогу зі стану неправильна, беремо типову: {custom}");
        }

        return new Uri(KastomEndpoints.DefaultCatalogUrl);
    }

    private static ModuleVersion ReadLauncherVersion()
    {
        var text = typeof(LauncherServices).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ModuleVersion.TryParse(text, out var version) ? version : new ModuleVersion(0, 1, 0);
    }
}
