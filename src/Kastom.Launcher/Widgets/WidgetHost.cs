using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;
using Kastom.Launcher.Settings;
using Kastom.Sdk.Services;

namespace Kastom.Launcher.Widgets;

/// <summary>
/// Вікна віджетів: власний віджет лаунчера ("kastom/modules") і віджети модулів ("&lt;moduleId&gt;/&lt;widgetId&gt;").
/// Розташування й видимість зберігаються в LauncherState.Widgets.
/// </summary>
public sealed class WidgetHost
{
    public const string LauncherWidgetKey = "kastom/modules";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILauncherStateStore _state;
    private readonly IModuleManager _modules;
    private readonly ScreenPolicy _screens;
    private readonly IModuleLogger _log;
    private readonly Func<FrameworkElement> _createModulesQuickView;
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private DesktopLayer? _desktopLayer;
    private bool _restored;
    private bool _closed;

    public WidgetHost(
        ILauncherStateStore state,
        IModuleManager modules,
        ScreenPolicy screens,
        IModuleLogger log,
        Func<FrameworkElement> createModulesQuickView)
    {
        _state = state;
        _modules = modules;
        _screens = screens;
        _log = log;
        _createModulesQuickView = createModulesQuickView;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Один таймер на всі віджети: зупинений, коли нічого не змінюється.
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();

        _modules.Changed += OnModulesChanged;
    }

    /// <summary>Віджет показано чи сховано (аргумент — ключ віджета).</summary>
    public event EventHandler<string>? VisibilityChanged;

    /// <summary>Відкрити всі віджети, що були видимі, якщо їхній вміст зараз можна створити.</summary>
    public void RestoreVisibleWidgets()
    {
        _restored = true;
        OpenVisiblePlacements();
    }

    public bool IsVisible(string widgetKey) => _windows.ContainsKey(widgetKey);

    public void Show(string widgetKey)
    {
        if (_closed)
        {
            return;
        }

        if (_windows.TryGetValue(widgetKey, out var existing))
        {
            // Віджет на шарі столу фокус не бере: інакше Win+D знову віддав би фокус йому й не повернув вікна.
            if (_screens.MayActivate && !_state.State.WidgetsOnDesktop) existing.Activate();
            return;
        }

        if (Open(widgetKey))
        {
            SaveNow();
        }
    }

    public void Hide(string widgetKey)
    {
        var wasOpen = _windows.Remove(widgetKey, out var window);
        if (window is not null)
        {
            window.TrackChanges = false;
            window.Close();
        }

        var placement = _state.State.Widgets.GetValueOrDefault(widgetKey);
        if (placement is { Visible: true })
        {
            placement.Visible = false;
            SaveNow();
        }

        if (wasOpen)
        {
            VisibilityChanged?.Invoke(this, widgetKey);
        }
    }

    public void Toggle(string widgetKey)
    {
        if (IsVisible(widgetKey))
        {
            Hide(widgetKey);
        }
        else
        {
            Show(widgetKey);
        }
    }

    public bool OnDesktop => _state.State.WidgetsOnDesktop;

    /// <summary>Увімкнути чи вимкнути шар робочого столу (🟡): відкриті віджети перевідкриваються з новим власником.</summary>
    public void SetOnDesktop(bool onDesktop)
    {
        if (_state.State.WidgetsOnDesktop == onDesktop)
        {
            return;
        }

        _state.State.WidgetsOnDesktop = onDesktop;
        var keys = _windows.Keys.ToList();
        foreach (var key in keys)
        {
            CloseKeepingVisible(key);
        }

        SaveNow();
        foreach (var key in keys)
        {
            if (!Open(key))
            {
                VisibilityChanged?.Invoke(this, key);
            }
        }

        if (!onDesktop)
        {
            _desktopLayer?.Dispose();
            _desktopLayer = null;
        }
    }

    /// <summary>
    /// Вихід: закрити всі вікна, не міняючи Visible, — при наступному запуску вони відкриються знову.
    /// Після цього хост нічого не відкриває сам: модулі, що зупиняються слідом, не повернуть віджети на екран.
    /// </summary>
    public void CloseAll()
    {
        _closed = true;
        _restored = false;
        if (_desktopLayer is not null)
        {
            _desktopLayer.ShellRestarted -= OnShellRestarted;
            _desktopLayer.Dispose();
            _desktopLayer = null;
        }

        var keys = _windows.Keys.ToList();
        foreach (var key in keys)
        {
            CloseKeepingVisible(key);
        }

        if (keys.Count > 0)
        {
            SaveNow();
            foreach (var key in keys)
            {
                VisibilityChanged?.Invoke(this, key);
            }
        }
    }

    private bool Open(string key)
    {
        if (_windows.ContainsKey(key))
        {
            return true;
        }

        if (_closed)
        {
            return false;
        }

        if (!TryResolve(key, out var spec))
        {
            return false;
        }

        FrameworkElement content;
        try
        {
            content = spec.CreateContent();
        }
        catch (Exception ex)
        {
            content = CreateErrorContent(ex);
        }

        var placement = _state.State.Widgets.GetValueOrDefault(key);
        var hasPlacement = placement is not null
            && double.IsFinite(placement.Left) && double.IsFinite(placement.Top)
            && double.IsFinite(placement.Width) && double.IsFinite(placement.Height)
            && placement.Width > 0 && placement.Height > 0
            // Режим перевірки: місце поза тестовою областю (з минулих запусків) не беремо, щоб не з'явитися на чужому екрані.
            && (!_screens.IsTestScreen || _screens.WorkArea.Contains(new Rect(placement.Left, placement.Top, placement.Width, placement.Height)));

        var width = hasPlacement ? placement!.Width : spec.Width;
        var height = hasPlacement ? placement!.Height : spec.Height;
        double left, top;
        if (hasPlacement)
        {
            (left, top) = (placement!.Left, placement.Top);
        }
        else
        {
            // Нове вікно: у верхньому правому куті робочої області, кожне наступне трохи нижче-лівіше.
            var area = _screens.WorkArea;
            var shift = _windows.Count % 8 * 28;
            left = area.Right - width - 24 - shift;
            top = area.Top + 24 + shift;
        }

        (left, top, width, height) = Clamp(left, top, width, height, _screens.IsTestScreen ? _screens.WorkArea : null);

        var window = new WidgetWindow(spec.Title, content)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            Tag = key,
        };
        if (_state.State.WidgetsOnDesktop)
        {
            AttachToDesktop(window, key);
        }

        window.Closed += OnWindowClosed;
        window.PlacementChanged += OnWindowPlacementChanged;
        _windows[key] = window;

        placement ??= new WidgetPlacement();
        placement.Visible = true;
        (placement.Left, placement.Top, placement.Width, placement.Height) = (left, top, width, height);
        _state.State.Widgets[key] = placement;

        window.Show();
        window.TrackChanges = true;
        VisibilityChanged?.Invoke(this, key);
        return true;
    }

    private void AttachToDesktop(WidgetWindow window, string key)
    {
        try
        {
            if (_desktopLayer is null)
            {
                _desktopLayer = new DesktopLayer();
                _desktopLayer.ShellRestarted += OnShellRestarted;
            }

            if (!DesktopLayer.TryAttach(window))
            {
                _log.Warn($"Віджет {key}: вікна робочого столу не знайдено, відкриваю звичайним вікном.");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Віджет {key}: шар робочого столу недоступний ({ex.Message}), відкриваю звичайним вікном.");
        }
    }

    /// <summary>
    /// Провідник перезапустився. Вікна віджетів з іншого процесу Windows зазвичай не знищує, а лише забирає їм власника,
    /// і тоді Win+D їх ховає; тож перевідкриваємо всі відкриті віджети з новим Progman і повертаємо ті, що зникли.
    /// </summary>
    private void OnShellRestarted(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() => ReattachToDesktop(attempt: 1));
    }

    /// <summary>
    /// Новий Progman з'являється не обов'язково раніше за TaskbarCreated: якщо його ще немає, пробуємо ще кілька разів
    /// з паузою (одноразові таймери: 1, 2, 4, 8 с), а потім відкриваємо віджети звичайними вікнами.
    /// </summary>
    private void ReattachToDesktop(int attempt)
    {
        if (!_restored || _closed || !_state.State.WidgetsOnDesktop)
        {
            return;
        }

        if (!DesktopLayer.DesktopExists() && attempt < 5)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1 << (attempt - 1)) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ReattachToDesktop(attempt + 1);
            };
            timer.Start();
            return;
        }

        var keys = _windows.Keys.ToList();
        foreach (var key in keys)
        {
            CloseKeepingVisible(key);
        }

        SaveNow();
        OpenVisiblePlacements();
        foreach (var key in keys.Where(k => !_windows.ContainsKey(k)))
        {
            VisibilityChanged?.Invoke(this, key);
        }
    }

    private void OpenVisiblePlacements()
    {
        var keys = _state.State.Widgets.Where(p => p.Value.Visible).Select(p => p.Key).ToList();
        foreach (var key in keys)
        {
            if (!_windows.ContainsKey(key))
            {
                Open(key);
            }
        }
    }

    private void OnModulesChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            Reconcile();
        }
        else
        {
            _dispatcher.BeginInvoke(Reconcile);
        }
    }

    /// <summary>Закрити вікна модулів, що зупинилися (Visible лишається), і відкрити ті, що знову можуть працювати.</summary>
    private void Reconcile()
    {
        var stopped = _windows.Keys.Where(k => k != LauncherWidgetKey && !TryResolve(k, out _)).ToList();
        foreach (var key in stopped)
        {
            CloseKeepingVisible(key);
        }

        if (stopped.Count > 0)
        {
            SaveNow();
            foreach (var key in stopped)
            {
                VisibilityChanged?.Invoke(this, key);
            }
        }

        if (_restored)
        {
            OpenVisiblePlacements();
        }
    }

    private bool TryResolve(string key, out WidgetSpec spec)
    {
        if (key == LauncherWidgetKey)
        {
            spec = new WidgetSpec(UiStrings.LauncherWidgetTitle, 340, 480, _createModulesQuickView);
            return true;
        }

        spec = default;
        var slash = key.IndexOf('/');
        if (slash <= 0 || slash == key.Length - 1)
        {
            return false;
        }

        var moduleId = key[..slash];
        var widgetId = key[(slash + 1)..];
        var module = _modules.Modules.FirstOrDefault(m => m.Id == moduleId);
        if (module is not { IsRunning: true })
        {
            return false;
        }

        var definition = _modules.GetWidgets(moduleId).FirstOrDefault(w => w.Id == widgetId);
        if (definition is null)
        {
            return false;
        }

        spec = new WidgetSpec(definition.Title, definition.DefaultWidth, definition.DefaultHeight, definition.CreateContent);
        return true;
    }

    private void CloseKeepingVisible(string key)
    {
        if (!_windows.Remove(key, out var window))
        {
            return;
        }

        window.TrackChanges = false;
        Capture(key, window);
        window.Close();
    }

    /// <summary>Користувач закрив вікно («×», Alt+F4): віджет більше не видимий.</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not WidgetWindow window || window.Tag is not string key)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        window.PlacementChanged -= OnWindowPlacementChanged;
        if (!_windows.TryGetValue(key, out var current) || !ReferenceEquals(current, window))
        {
            return;
        }

        _windows.Remove(key);
        if (window.LostOwner)
        {
            // Користувач віджет не закривав: Visible лишається, віджет повернеться після TaskbarCreated.
            _log.Info($"Віджет {key} зник разом з робочим столом; повернеться, коли Провідник запуститься знову.");
            SaveNow();
            VisibilityChanged?.Invoke(this, key);
            return;
        }

        if (_state.State.Widgets.TryGetValue(key, out var placement))
        {
            placement.Visible = false;
        }

        SaveNow();
        VisibilityChanged?.Invoke(this, key);
    }

    private void OnWindowPlacementChanged(object? sender, EventArgs e)
    {
        if (sender is WidgetWindow { Tag: string key } window)
        {
            Capture(key, window);
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void Capture(string key, WidgetWindow window)
    {
        if (window.WindowState != WindowState.Normal || !window.IsLoaded)
        {
            return;
        }

        if (!_state.State.Widgets.TryGetValue(key, out var placement))
        {
            placement = new WidgetPlacement { Visible = true };
            _state.State.Widgets[key] = placement;
        }

        placement.Left = window.Left;
        placement.Top = window.Top;
        placement.Width = window.ActualWidth;
        placement.Height = window.ActualHeight;
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _state.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не вдалося записати стан: віджети працюють далі, наступна зміна спробує знову.
        }
    }

    /// <summary>
    /// Вмістити вікно у віртуальний екран, щоб віджет не відкрився за межами моніторів;
    /// у режимі перевірки — у тестову область (вона може лежати й поза моніторами).
    /// </summary>
    private static (double Left, double Top, double Width, double Height) Clamp(double left, double top, double width, double height, Rect? area)
    {
        var bounds = area ?? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var screenLeft = bounds.Left;
        var screenTop = bounds.Top;
        var screenWidth = bounds.Width;
        var screenHeight = bounds.Height;

        width = Math.Min(width, screenWidth);
        height = Math.Min(height, screenHeight);
        left = Math.Clamp(left, screenLeft, screenLeft + screenWidth - width);
        top = Math.Clamp(top, screenTop, screenTop + screenHeight - height);
        return (left, top, width, height);
    }

    private static FrameworkElement CreateErrorContent(Exception exception)
    {
        var text = UiTheme.Label(string.Format(UiStrings.WidgetContentErrorFormat, UiTheme.FormatError(exception)), 12);
        text.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        text.Margin = new Thickness(4);
        return text;
    }

    private readonly record struct WidgetSpec(string Title, double Width, double Height, Func<FrameworkElement> CreateContent);
}
