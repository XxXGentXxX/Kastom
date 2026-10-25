using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;

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
    private readonly Func<FrameworkElement> _createModulesQuickView;
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private bool _restored;

    public WidgetHost(ILauncherStateStore state, IModuleManager modules, Func<FrameworkElement> createModulesQuickView)
    {
        _state = state;
        _modules = modules;
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
        if (_windows.TryGetValue(widgetKey, out var existing))
        {
            existing.Activate();
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

    /// <summary>Закрити всі вікна, не міняючи Visible: при наступному запуску вони відкриються знову.</summary>
    public void CloseAll()
    {
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
            && placement.Width > 0 && placement.Height > 0;

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
            var area = SystemParameters.WorkArea;
            var shift = _windows.Count % 8 * 28;
            left = area.Right - width - 24 - shift;
            top = area.Top + 24 + shift;
        }

        (left, top, width, height) = Clamp(left, top, width, height);

        var window = new WidgetWindow(spec.Title, content)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            Tag = key,
        };
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

    /// <summary>Вмістити вікно в віртуальний екран, щоб віджет не відкрився за межами моніторів.</summary>
    private static (double Left, double Top, double Width, double Height) Clamp(double left, double top, double width, double height)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenWidth = SystemParameters.VirtualScreenWidth;
        var screenHeight = SystemParameters.VirtualScreenHeight;

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
