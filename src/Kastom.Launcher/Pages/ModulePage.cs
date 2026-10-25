using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Hotkeys;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;
using Kastom.Launcher.Shell;
using Kastom.Launcher.Widgets;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Settings;

namespace Kastom.Launcher.Pages;

/// <summary>
/// Сторінка одного модуля. Якщо в модуля є власна сторінка, угорі дві вкладки: «Модуль» (власна сторінка) і «Налаштування»;
/// якщо немає, одразу налаштування. «Налаштування» — налаштування модуля, гарячі клавіші, віджети, дії.
/// </summary>
public sealed class ModulePage : UserControl
{
    private readonly string _moduleId;
    private readonly IModuleManager _modules;
    private readonly IActionCatalog _actions;
    private readonly WidgetHost _widgets;
    private readonly IHotkeyManager? _hotkeys;
    private readonly StackPanel _root = new() { Margin = new Thickness(24) };
    private readonly Dictionary<string, ToggleButton> _widgetToggles = new();
    private readonly ContentControl _moduleTabHost = new();
    private readonly ContentControl _settingsTabHost = new();
    private string? _signature;
    private bool _settingsSelected;
    private TabControl? _tabs;

    // Вміст вкладки «Налаштування» будується з цих даних щоразу, коли вкладку відкривають.
    private ISettingsRegistry? _settings;
    private IReadOnlyList<Kastom.Sdk.Widgets.WidgetDefinition> _widgetDefinitions = [];
    private IReadOnlyList<RegisteredAction> _moduleActions = [];

    public string ModuleId => _moduleId;

    public ModulePage(
        string moduleId,
        IModuleManager modules,
        IActionCatalog actions,
        WidgetHost widgets,
        IHotkeyManager? hotkeys,
        bool openSettings = false)
    {
        _moduleId = moduleId;
        _modules = modules;
        _actions = actions;
        _widgets = widgets;
        _hotkeys = hotkeys;
        _settingsSelected = openSettings;

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };

        Rebuild();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();
        _modules.Changed += OnStateChanged;
        _actions.Changed += OnStateChanged;
        _widgets.VisibilityChanged += OnWidgetVisibilityChanged;
        Rebuild();
        SyncWidgetToggles();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unsubscribe();

        // Власну сторінку й вміст створив модуль: не тримаємо їх у дереві, щоб модуль не утримував вікно лаунчера.
        // Наступний Loaded побудує сторінку заново (фабрику SDK викликає щоразу, коли сторінку відкривають).
        _root.Children.Clear();
        _moduleTabHost.Content = null;
        _settingsTabHost.Content = null;
        _tabs = null;
        _widgetToggles.Clear();
        _signature = null;
    }

    /// <summary>Відкрити вкладку «Налаштування»; без вкладок (у модуля немає власної сторінки) там і так одразу налаштування.</summary>
    public void ShowSettings()
    {
        _settingsSelected = true;
        if (_tabs is not null) _tabs.SelectedIndex = 1;
    }

    private void Unsubscribe()
    {
        _modules.Changed -= OnStateChanged;
        _actions.Changed -= OnStateChanged;
        _widgets.VisibilityChanged -= OnWidgetVisibilityChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            Rebuild();
        }
        else
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
    }

    private void OnWidgetVisibilityChanged(object? sender, string key)
    {
        if (Dispatcher.CheckAccess())
        {
            SyncWidgetToggles();
        }
        else
        {
            Dispatcher.BeginInvoke(SyncWidgetToggles);
        }
    }

    private void SyncWidgetToggles()
    {
        foreach (var (key, toggle) in _widgetToggles)
        {
            toggle.IsChecked = _widgets.IsVisible(key);
        }
    }

    private void Rebuild()
    {
        var info = _modules.Modules.FirstOrDefault(m => m.Id == _moduleId);
        var running = info is { IsInstalled: true, IsEnabled: true, IsRunning: true };
        var settings = running ? _modules.GetSettings(_moduleId) : null;
        var widgetDefinitions = running ? _modules.GetWidgets(_moduleId) : [];
        var moduleActions = running ? _actions.All.Where(a => a.ModuleId == _moduleId).ToList() : [];

        // Сторінку перебудовуємо лише коли щось справді змінилося: власна сторінка модуля й поля вводу не мають скидатися.
        var signature = string.Join(
            "|",
            info?.Name,
            info?.Description,
            info?.InstalledVersion,
            info?.IsEnabled,
            info?.IsRunning,
            info?.Error,
            info?.Icon?.Length,
            settings is null ? 0 : RuntimeHelpers.GetHashCode(settings),
            string.Join(",", widgetDefinitions.Select(w => w.Id)),
            string.Join(",", moduleActions.Select(a => a.GlobalId)));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        _root.Children.Clear();
        _moduleTabHost.Content = null;
        _settingsTabHost.Content = null;
        _tabs = null;
        _widgetToggles.Clear();

        if (info is null)
        {
            _root.Children.Add(UiTheme.Label(UiStrings.ModuleNotFound, 14, secondary: true));
            return;
        }

        _root.Children.Add(CreateHeader(info));

        if (!info.IsInstalled)
        {
            _root.Children.Add(UiTheme.Label(UiStrings.ModuleNotInstalled, 14, secondary: true));
            return;
        }

        if (!running)
        {
            _root.Children.Add(UiTheme.Label(info.IsEnabled ? UiStrings.ModuleNotRunning : UiStrings.ModuleDisabled, 14, secondary: true));
            return;
        }

        _settings = settings;
        _widgetDefinitions = widgetDefinitions;
        _moduleActions = moduleActions;

        if (settings?.CustomPageFactory is null)
        {
            _root.Children.Add(CreateSettingsTab());
            SyncWidgetToggles();
            return;
        }

        var tabs = new TabControl { Padding = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        tabs.Items.Add(new TabItem { Header = UiStrings.ModuleTabModule, Content = _moduleTabHost });
        tabs.Items.Add(new TabItem { Header = UiStrings.ModuleTabSettings, Content = _settingsTabHost });
        tabs.SelectedIndex = _settingsSelected ? 1 : 0;
        tabs.SelectionChanged += (_, e) =>
        {
            // Вибір у списках усередині вкладок теж спливає сюди; нам потрібна лише зміна самої вкладки.
            if (!ReferenceEquals(e.OriginalSource, tabs)) return;
            _settingsSelected = tabs.SelectedIndex == 1;
            ShowSelectedTab();
        };
        _tabs = tabs;
        _root.Children.Add(tabs);
        ShowSelectedTab();
    }

    // Невибрану вкладку звільняємо: власну сторінку модуля (мікшер із таймерами) не тримаємо, поки її не видно.
    private void ShowSelectedTab()
    {
        _widgetToggles.Clear();
        if (_settingsSelected)
        {
            _moduleTabHost.Content = null;
            _settingsTabHost.Content = CreateSettingsTab();
            SyncWidgetToggles();
        }
        else
        {
            _settingsTabHost.Content = null;
            _moduleTabHost.Content = CreateCustomPage(_settings?.CustomPageFactory);
        }
    }

    private UIElement CreateSettingsTab()
    {
        var tab = new StackPanel();
        tab.Children.Add(CreateSection(UiStrings.SettingsSection, CreateSettingsContent(_settings)));
        if (_hotkeys is not null && _moduleActions.Count > 0)
            tab.Children.Add(CreateSection(UiStrings.HotkeysSection, new ModuleHotkeysEditor(_hotkeys, _moduleId)));
        tab.Children.Add(CreateSection(UiStrings.WidgetsSection, CreateWidgetsContent(_widgetDefinitions)));
        tab.Children.Add(CreateSection(UiStrings.ActionsSection, CreateActionsContent(_moduleActions)));
        return tab;
    }

    private static UIElement CreateHeader(ModuleInfo info)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new ModuleIconView
        {
            Icon = info.Icon,
            ModuleName = info.Name,
            Size = 24,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(UiTheme.Label(info.Name, 24, semibold: true));
        panel.Children.Add(title);

        var status = !info.IsInstalled
            ? UiStrings.ModuleNotInstalled
            : !info.IsEnabled ? UiStrings.QuickStatusDisabled
            : info.IsRunning ? UiStrings.QuickStatusRunning : UiStrings.QuickStatusNotRunning;
        var line = string.IsNullOrEmpty(info.InstalledVersion)
            ? status
            : $"{string.Format(UiStrings.VersionFormat, info.InstalledVersion)} · {status}";
        var meta = UiTheme.Label(line, 13, secondary: true);
        meta.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(meta);

        if (!string.IsNullOrWhiteSpace(info.Description))
        {
            var description = UiTheme.Label(info.Description, 14);
            description.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(description);
        }

        if (!string.IsNullOrWhiteSpace(info.Error))
        {
            var error = UiTheme.ErrorLabel();
            UiTheme.ShowError(error, string.Format(UiStrings.ModuleErrorFormat, info.Error));
            panel.Children.Add(error);
        }

        return panel;
    }

    private static UIElement CreateCustomPage(Func<UIElement>? factory)
    {
        if (factory is null)
        {
            return new Border();
        }

        UIElement element;
        try
        {
            element = factory();
        }
        catch (Exception ex)
        {
            var error = UiTheme.ErrorLabel();
            UiTheme.ShowError(error, string.Format(UiStrings.CustomPageErrorFormat, UiTheme.FormatError(ex)));
            element = error;
        }

        if (element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 0, 0, 16);
        }

        return element;
    }

    private static UIElement CreateSection(string title, UIElement content)
    {
        var body = new StackPanel();
        var header = UiTheme.Label(title, 16, semibold: true);
        header.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(header);
        body.Children.Add(content);
        return UiTheme.Card(body, new Thickness(0, 0, 0, 12));
    }

    private UIElement CreateSettingsContent(ISettingsRegistry? settings)
    {
        if (settings is null || settings.Definitions.Count == 0)
        {
            return UiTheme.Label(UiStrings.ModuleHasNoSettings, 13, secondary: true);
        }

        return new SettingsView(settings, _moduleId, _actions, compact: false);
    }

    private UIElement CreateWidgetsContent(IReadOnlyList<Kastom.Sdk.Widgets.WidgetDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            return UiTheme.Label(UiStrings.ModuleNoWidgets, 13, secondary: true);
        }

        var list = new StackPanel();
        foreach (var definition in definitions)
        {
            var key = $"{_moduleId}/{definition.Id}";

            var title = UiTheme.Label(definition.Title, 14);
            title.VerticalAlignment = VerticalAlignment.Center;
            var caption = UiTheme.Label(UiStrings.WidgetOnDesktop, 13, secondary: true);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Margin = new Thickness(16, 0, 10, 0);

            var toggle = new SwitchToggle { Style = UiTheme.SwitchStyle };
            AutomationProperties.SetName(toggle, $"{definition.Title}: {UiStrings.WidgetOnDesktop}");
            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true)
                {
                    _widgets.Show(key);
                }
                else
                {
                    _widgets.Hide(key);
                }

                toggle.IsChecked = _widgets.IsVisible(key);
            };
            _widgetToggles[key] = toggle;

            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(title);
            Grid.SetColumn(caption, 1);
            row.Children.Add(caption);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            list.Children.Add(row);
        }

        return list;
    }

    private UIElement CreateActionsContent(IReadOnlyList<RegisteredAction> moduleActions)
    {
        if (moduleActions.Count == 0)
        {
            return UiTheme.Label(UiStrings.ModuleNoActions, 13, secondary: true);
        }

        var list = new StackPanel();
        foreach (var registered in moduleActions)
        {
            var error = UiTheme.ErrorLabel();

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(UiTheme.Label(registered.Action.Title, 14));
            if (!string.IsNullOrWhiteSpace(registered.Action.Description))
            {
                texts.Children.Add(UiTheme.Label(registered.Action.Description, 12, secondary: true));
            }

            var run = new Button { Content = UiStrings.RunAction, Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(run, $"{UiStrings.RunAction}: {registered.Action.Title}");
            run.Click += async (_, _) =>
            {
                run.IsEnabled = false;
                try
                {
                    await _actions.ExecuteAsync(registered.GlobalId, ActionTrigger.Ui);
                    UiTheme.ShowError(error, null);
                }
                catch (Exception ex)
                {
                    UiTheme.ShowError(error, string.Format(UiStrings.ActionFailedFormat, UiTheme.FormatError(ex)));
                }
                finally
                {
                    run.IsEnabled = true;
                }
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(texts);
            Grid.SetColumn(run, 1);
            row.Children.Add(run);

            var item = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            item.Children.Add(row);
            item.Children.Add(error);
            list.Children.Add(item);
        }

        return list;
    }
}
