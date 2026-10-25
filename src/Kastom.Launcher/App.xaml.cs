using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Kastom.Core;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;
using Kastom.Launcher.Shell;
using Kastom.Launcher.Widgets;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstance? _instance;
    private readonly List<string> _startupProblems = [];

    private LauncherServices? _services;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private QuickPanelWindow? _quickPanel;
    private long _quickPanelClosedAt;
    private Rect? _mainBounds;
    private WindowState _mainState = WindowState.Normal;
    private HashSet<string> _announcedUpdates = [];
    private string? _renderTempDirectory;
    // Звичайний вихід: до 1 с чекаємо черги операцій, решту часу (модулі зупиняються паралельно по 5 с) — на зупинку.
    private static readonly TimeSpan ExitGateWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExitStopBudget = TimeSpan.FromSeconds(7);

    // Вихід із Windows: усе має вкластися в ~3,5 с, бо після ~5 с Windows показує екран «програма заважає вимкненню».
    private static readonly TimeSpan SessionGateWait = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan SessionStopBudget = TimeSpan.FromSeconds(3);

    private bool _exiting;
    private HashSet<string> _announcedRestart = [];

    public App(LaunchOptions options, SingleInstance? instance)
    {
        _options = options;
        _instance = instance;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (_options.RenderPagesDirectory is { } renderDirectory)
        {
            _ = RenderAndExitAsync(renderDirectory);
            return;
        }

        _instance?.StartListening(OnPipeMessage);

        try
        {
            _services = new LauncherServices(_options, KastomPaths.Default(), () => ExitAsync(), id => OpenMainWindow(id));
        }
        catch (Exception exception)
        {
            Trace.TraceError(exception.ToString());
            MessageBox.Show(string.Format(Strings.StartupFatal, exception.Message), Strings.AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var services = _services;
        Guard("автозапуск", () => ApplyAutostart(services));
        Guard("дії лаунчера", RegisterLauncherActions);
        Guard("гарячі клавіші", () => services.CreateHotkeys(registerInSystem: true));
        Guard("значок у треї", CreateTray);
        Guard("оновлення (підписка)", () => services.Updates.StateChanged += OnUpdatesStateChanged);

        if (!_options.Minimized) OpenMainWindow();
        _ = StartBackgroundServicesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        _services?.Dispose();
        _instance?.Dispose();
        if (_renderTempDirectory is not null)
        {
            try { Directory.Delete(_renderTempDirectory, true); }
            catch (Exception) { }
        }

        base.OnExit(e);
    }

    // ---------- Запуск ----------

    private void Guard(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogError($"Запуск: крок «{step}» не вдався", exception);
            _startupProblems.Add(step);
        }
    }

    private async Task StartBackgroundServicesAsync()
    {
        var services = _services!;
        try
        {
            await services.Modules.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogError("Запуск: модулі не стартували", exception);
            _startupProblems.Add("модулі");
        }

        if (_exiting) return;
        Guard("оновлення", services.Updates.Start);
        Guard("віджети", services.Widgets.RestoreVisibleWidgets);

        if (_startupProblems.Count > 0)
        {
            Notify(string.Format(Strings.StartupProblems, string.Join(", ", _startupProblems)));
            _startupProblems.Clear();
        }
    }

    private static void ApplyAutostart(LauncherServices services) =>
        services.Autostart.Apply(services.StateStore.State.Autostart);

    private void RegisterLauncherActions()
    {
        var actions = _services!.Actions;
        const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

        actions.RegisterLauncherAction(new ModuleAction
        {
            Id = "open",
            Title = Strings.ActionOpenTitle,
            DefaultHotkey = new HotkeyGesture(CtrlAlt, 'K'),
            Execute = _ =>
            {
                OpenMainWindow();
                return Task.CompletedTask;
            },
        });
        actions.RegisterLauncherAction(new ModuleAction
        {
            Id = "quick-panel",
            Title = Strings.ActionQuickPanelTitle,
            DefaultHotkey = new HotkeyGesture(CtrlAlt, 'Q'),
            Execute = _ =>
            {
                ToggleQuickPanel();
                return Task.CompletedTask;
            },
        });
        actions.RegisterLauncherAction(new ModuleAction
        {
            Id = "toggle-widget",
            Title = Strings.ActionToggleWidgetTitle,
            Execute = _ =>
            {
                ToggleLauncherWidget();
                return Task.CompletedTask;
            },
        });
        actions.RegisterLauncherAction(new ModuleAction
        {
            Id = "check-updates",
            Title = Strings.ActionCheckUpdatesTitle,
            Execute = _ => CheckUpdatesAsync(),
        });
    }

    private void CreateTray()
    {
        _tray = new TrayIcon(
            openMainWindow: () => OpenMainWindow(),
            toggleQuickPanel: ToggleQuickPanel,
            isWidgetVisible: () => _services!.Widgets.IsVisible(WidgetHost.LauncherWidgetKey),
            toggleWidget: ToggleLauncherWidget,
            checkUpdates: () => _ = CheckUpdatesAsync(),
            exit: () => _ = ExitAsync(),
            balloonClicked: () => OpenMainWindow(showModules: true));
    }

    // ---------- Вікна ----------

    public void OpenMainWindow(string? moduleId = null, bool showModules = false)
    {
        if (_services is null || _exiting) return;
        try
        {
            if (_mainWindow is null)
            {
                _mainWindow = CreateMainWindow();
                _mainWindow.Show();
            }

            var window = _mainWindow;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            if (showModules) window.NavigateTo(NavPage.Modules);
            if (!string.IsNullOrEmpty(moduleId)) window.OpenModulePage(moduleId);

            // Windows не віддає фокус фоновому процесові; кадр поверх усіх обходить це.
            window.Topmost = true;
            window.Topmost = false;
            window.Activate();
        }
        catch (Exception exception)
        {
            LogError("Не вдалося відкрити головне вікно", exception);
            Notify(string.Format(Strings.UnhandledError, exception.Message));
        }
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_services!, () => _ = ExitAsync(restart: true));
        if (_mainBounds is { } bounds && IsOnScreen(bounds))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
        }

        if (_mainState == WindowState.Maximized) window.WindowState = WindowState.Maximized;

        window.Closing += (_, _) =>
        {
            _mainBounds = window.RestoreBounds;
            _mainState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        };
        window.Closed += (_, _) =>
        {
            // Жодних посилань на вікно: воно має потрапити в збирач сміття.
            if (ReferenceEquals(_mainWindow, window)) _mainWindow = null;
            if (ReferenceEquals(MainWindow, window)) MainWindow = null;
        };
        return window;
    }

    private static bool IsOnScreen(Rect bounds)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var overlap = Rect.Intersect(screen, bounds);
        return !overlap.IsEmpty && overlap.Width >= 100 && overlap.Height >= 50;
    }

    private void ToggleQuickPanel()
    {
        if (_services is null || _exiting) return;
        try
        {
            if (_quickPanel is not null)
            {
                _quickPanel.Close();
                return;
            }

            // Клік по значку закриває панель втратою фокусу ще до самого кліка; повторно не відкриваємо.
            if (Stopwatch.GetElapsedTime(_quickPanelClosedAt).TotalMilliseconds < 300) return;

            var services = _services;
            var panel = new QuickPanelWindow(new ModulesQuickView(services.Modules, services.Actions, id => OpenMainWindow(id)));
            panel.Closed += (_, _) =>
            {
                if (ReferenceEquals(_quickPanel, panel)) _quickPanel = null;
                _quickPanelClosedAt = Stopwatch.GetTimestamp();
            };
            _quickPanel = panel;
            panel.ShowNearTray();
        }
        catch (Exception exception)
        {
            _quickPanel = null;
            LogError("Не вдалося відкрити швидку панель", exception);
            Notify(string.Format(Strings.QuickPanelFailed, exception.Message));
        }
    }

    private void ToggleLauncherWidget()
    {
        try
        {
            _services!.Widgets.Toggle(WidgetHost.LauncherWidgetKey);
        }
        catch (Exception exception)
        {
            LogError("Не вдалося перемкнути віджет лаунчера", exception);
            Notify(string.Format(Strings.UnhandledError, exception.Message));
        }
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            await _services!.Updates.CheckNowAsync();
            Notify(Strings.UpdateCheckDone);
        }
        catch (Exception exception)
        {
            LogError("Не вдалося перевірити оновлення", exception);
            Notify(string.Format(Strings.UnhandledError, exception.Message));
        }
    }

    // ---------- Оновлення: одна підказка на кожен новий набір ----------

    private void OnUpdatesStateChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(AnnounceUpdates);

    private void AnnounceUpdates()
    {
        if (_tray is null || _services is null || _exiting) return;

        AnnounceRestartRequired();

        var current = new List<(string Key, string Label)>();
        try
        {
            var modules = _services.Modules.Modules;
            foreach (var id in _services.Updates.ModulesWithUpdates)
            {
                var info = modules.FirstOrDefault(module => module.Id == id);
                current.Add(($"module:{id}:{info?.AvailableVersion}", info?.Name ?? id));
            }

            if (_services.Updates.LauncherAvailableVersion is { } launcherVersion)
                current.Add(($"launcher:{launcherVersion}", string.Format(Strings.BalloonLauncherVersion, launcherVersion)));
        }
        catch (Exception exception)
        {
            LogError("Не вдалося скласти перелік оновлень", exception);
            return;
        }

        var fresh = current.Where(item => !_announcedUpdates.Contains(item.Key)).ToList();
        _announcedUpdates = current.Select(item => item.Key).ToHashSet();
        if (fresh.Count == 0) return;

        try
        {
            _tray.ShowBalloon(Strings.BalloonUpdatesTitle,
                string.Format(Strings.BalloonUpdatesText, string.Join(", ", fresh.Select(item => item.Label))));
        }
        catch (Exception exception)
        {
            LogError("Не вдалося показати підказку про оновлення", exception);
        }
    }

    /// <summary>Автоматичне оновлення модулів: одна підказка, коли з'явилися модулі, що чекають на перезапуск.</summary>
    private void AnnounceRestartRequired()
    {
        HashSet<string> current;
        try
        {
            current = _services!.Modules.Modules.Where(module => module.RestartRequired).Select(module => module.Id).ToHashSet();
        }
        catch (Exception)
        {
            return;
        }

        var anyNew = current.Any(id => !_announcedRestart.Contains(id));
        _announcedRestart = current;
        if (!anyNew) return;

        try
        {
            _tray!.ShowBalloon(Strings.AppName, Strings.BalloonModulesRestart);
        }
        catch (Exception exception)
        {
            LogError("Не вдалося показати підказку про перезапуск", exception);
        }
    }

    // ---------- Вихід і перезапуск ----------

    /// <summary>Зупинити модулі й служби, закрити вікна й вийти; з <paramref name="restart"/> запустити лаунчер знову.</summary>
    public async Task ExitAsync(bool restart = false)
    {
        if (_exiting) return;
        _exiting = true;

        CloseWindows();
        await ShutdownServicesAsync(ExitGateWait, ExitStopBudget);

        if (restart)
        {
            // Mutex звільняємо до запуску нового процесу, інакше він вирішить, що лаунчер уже працює.
            _instance?.Dispose();
            StartNewProcess();
        }

        Shutdown();
    }

    /// <summary>
    /// Вихід Windows (вимкнення, вихід із облікового запису): зупиняємо модулі, але не довше за ~3,5 с.
    /// Після цього обробника WPF сам викликає Shutdown() (якщо подію не скасовано), навіть коли користувач
    /// згодом скасує вихід із Windows, — тож лаунчер завершується, а не лишається напівзупиненим.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (_services is null || _exiting || e.Cancel) return;
        _exiting = true;

        CloseWindows();
        // .Wait() заблокував би потік інтерфейсу, а продовженням модулів потрібен диспетчер: качаємо повідомлення самі.
        var frame = new DispatcherFrame();
        Task.WhenAny(ShutdownServicesAsync(SessionGateWait, SessionStopBudget), Task.Delay(SessionGateWait + SessionStopBudget))
            .ContinueWith(_ => Dispatcher.InvokeAsync(() => { frame.Continue = false; }), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
    }

    private void CloseWindows()
    {
        try
        {
            _mainWindow?.Close();
            _quickPanel?.Close();
        }
        catch (Exception exception)
        {
            LogError("Не вдалося закрити вікна", exception);
        }
    }

    /// <summary>
    /// Порядок: скасувати операції користувача, зупинити перевірку оновлень, закрити віджети, зупинити модулі,
    /// зняти клавіші, прибрати значок у треї. HTTP-клієнт звільняється в OnExit.
    /// </summary>
    private async Task ShutdownServicesAsync(TimeSpan gateWait, TimeSpan stopBudget)
    {
        if (_services is { } services)
        {
            try { services.OperationsCts.Cancel(); }
            catch (Exception exception) { LogError("Не вдалося скасувати операції", exception); }

            services.DisposeUpdates();

            // Віджети закриваємо до модулів: розташування й видимість лишаються в стані, на наступний запуск.
            try
            {
                services.Widgets.CloseAll();
            }
            catch (Exception exception)
            {
                LogError("Не вдалося закрити віджети", exception);
            }

            try
            {
                // Токен обмежує лише очікування черги; самі модулі після нього зупиняються вже без черги.
                using var gateCts = new CancellationTokenSource(gateWait);
                await services.Modules.StopAllAsync(gateCts.Token).WaitAsync(gateWait + stopBudget);
            }
            catch (Exception exception)
            {
                LogError("Не вдалося зупинити модулі", exception);
            }

            services.DisposeHotkeys();
        }

        _tray?.Dispose();
        _tray = null;
    }

    private void StartNewProcess()
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var argument in RestartArguments()) info.ArgumentList.Add(argument);
            Process.Start(info);
        }
        catch (Exception exception)
        {
            LogError("Не вдалося перезапустити лаунчер", exception);
            MessageBox.Show(string.Format(Strings.RestartFailed, exception.Message), Strings.AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Аргументи для перезапуску: без --minimized (перезапуск показує вікно) і без --render-pages.</summary>
    private IEnumerable<string> RestartArguments()
    {
        var args = _options.RawArgs;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--minimized") continue;
            if (args[i] == "--render-pages")
            {
                i++;
                continue;
            }

            yield return args[i];
        }
    }

    // ---------- Помилки й повідомлення ----------

    private void OnPipeMessage(string line)
    {
        // "show" або "show:<id модуля>"
        var argument = line.Length > 5 && line[4] == ':' ? line[5..] : null;
        Dispatcher.InvokeAsync(() => OpenMainWindow(argument));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("Необроблений виняток у потоці інтерфейсу", e.Exception);
        Notify(string.Format(Strings.UnhandledError, e.Exception.Message));
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("Необроблений виняток у задачі", e.Exception);
        e.SetObserved();
        Dispatcher.InvokeAsync(() => Notify(string.Format(Strings.UnhandledError, e.Exception.InnerException?.Message ?? e.Exception.Message)));
    }

    private void LogError(string message, Exception exception)
    {
        if (_services is not null) _services.Log.Error(message, exception);
        else Trace.TraceError($"{message}: {exception}");
    }

    /// <summary>Коротка підказка: OSD, а якщо вона недоступна — повідомлення від значка в треї.</summary>
    public void Notify(string text)
    {
        try
        {
            if (_services is not null)
            {
                _services.Notifications.ShowOsd(text);
                return;
            }
        }
        catch (Exception)
        {
            // OSD недоступний: пробуємо значок у треї.
        }

        try
        {
            _tray?.ShowBalloon(Strings.AppName, text);
        }
        catch (Exception)
        {
        }
    }

    // ---------- Знімки сторінок ----------

    private async Task RenderAndExitAsync(string outputDirectory)
    {
        var code = 1;
        try
        {
            // Окрема тека: справжні дані користувача не чіпаємо.
            _renderTempDirectory = Path.Combine(Path.GetTempPath(), "KastomRender-" + Guid.NewGuid().ToString("N"));
            var paths = new KastomPaths(Path.Combine(_renderTempDirectory, "local"), Path.Combine(_renderTempDirectory, "roaming"));
            _services = new LauncherServices(_options, paths, () => Task.CompletedTask, _ => { });
            Guard("дії лаунчера", RegisterLauncherActions);
            Guard("гарячі клавіші", () => _services.CreateHotkeys(registerInSystem: false));
            code = await PageRenderer.RunAsync(_services, outputDirectory);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            Shutdown(code);
        }
    }
}
