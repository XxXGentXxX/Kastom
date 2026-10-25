using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;
using Kastom.Launcher.Widgets;
using Kastom.Sdk.Actions;

namespace Kastom.Launcher.Pages;

/// <summary>Сторінка одного модуля: власна сторінка, налаштування, віджети, дії.</summary>
public sealed class ModulePage : UserControl
{
    private readonly string _moduleId;
    private readonly IModuleManager _modules;
    private readonly IActionCatalog _actions;
    private readonly WidgetHost _widgets;
    private readonly StackPanel _root = new() { Margin = new Thickness(24) };
    private readonly Dictionary<string, ToggleButton> _widgetToggles = new();
    private string? _signature;

    public ModulePage(string moduleId, IModuleManager modules, IActionCatalog actions, WidgetHost widgets)
    {
        _moduleId = moduleId;
        _modules = modules;
        _actions = actions;
        _widgets = widgets;

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
        _widgetToggles.Clear();
        _signature = null;
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
            settings is null ? 0 : RuntimeHelpers.GetHashCode(settings),
            string.Join(",", widgetDefinitions.Select(w => w.Id)),
            string.Join(",", moduleActions.Select(a => a.GlobalId)));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        _root.Children.Clear();
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

        AddCustomPage(settings);
        AddSection(UiStrings.SettingsSection, CreateSettingsContent(settings));
        AddSection(UiStrings.WidgetsSection, CreateWidgetsContent(widgetDefinitions));
        AddSection(UiStrings.ActionsSection, CreateActionsContent(moduleActions));
        SyncWidgetToggles();
    }

    private static UIElement CreateHeader(ModuleInfo info)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(UiTheme.Label(info.Name, 24, semibold: true));

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

    private void AddCustomPage(Kastom.Sdk.Settings.ISettingsRegistry? settings)
    {
        var factory = settings?.CustomPageFactory;
        if (factory is null)
        {
            return;
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

        _root.Children.Add(element);
    }

    private void AddSection(string title, UIElement content)
    {
        var body = new StackPanel();
        var header = UiTheme.Label(title, 16, semibold: true);
        header.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(header);
        body.Children.Add(content);
        _root.Children.Add(UiTheme.Card(body, new Thickness(0, 0, 0, 12)));
    }

    private UIElement CreateSettingsContent(Kastom.Sdk.Settings.ISettingsRegistry? settings)
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
