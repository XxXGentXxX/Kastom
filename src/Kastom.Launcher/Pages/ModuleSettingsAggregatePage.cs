using System.Windows;
using System.Windows.Controls;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;
using Kastom.Sdk.Settings;

namespace Kastom.Launcher.Pages;

/// <summary>Вкладка «Налаштування модулів»: налаштування всіх увімкнених модулів на одній сторінці.</summary>
public sealed class ModuleSettingsAggregatePage : UserControl
{
    private readonly IModuleManager _modules;
    private readonly IActionCatalog _actions;
    private readonly Action<string> _openModulePage;
    private readonly StackPanel _sections = new();
    private List<(string Id, string Name, ISettingsRegistry Settings)> _shown = new();

    public ModuleSettingsAggregatePage(IModuleManager modules, IActionCatalog actions, Action<string> openModulePage)
    {
        _modules = modules;
        _actions = actions;
        _openModulePage = openModulePage;

        var title = UiTheme.Label(UiStrings.AggregateTitle, 24, semibold: true);
        title.Margin = new Thickness(0, 0, 0, 16);

        var page = new StackPanel { Margin = new Thickness(24) };
        page.Children.Add(title);
        page.Children.Add(_sections);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };

        Rebuild();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _modules.Changed -= OnModulesChanged;
        _modules.Changed += OnModulesChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _modules.Changed -= OnModulesChanged;

    private void OnModulesChanged(object? sender, EventArgs e)
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

    private void Rebuild()
    {
        var current = new List<(string Id, string Name, ISettingsRegistry Settings)>();
        foreach (var module in _modules.Modules.Where(m => m.IsEnabled))
        {
            if (_modules.GetSettings(module.Id) is { } settings)
            {
                current.Add((module.Id, module.Name, settings));
            }
        }

        // Налаштування не міняються між оновленнями — не перебудовуємо сторінку без потреби (користувач може редагувати поле).
        if (_sections.Children.Count > 0 && current.SequenceEqual(_shown))
        {
            return;
        }

        _shown = current;
        _sections.Children.Clear();

        if (current.Count == 0)
        {
            _sections.Children.Add(UiTheme.Label(UiStrings.AggregateEmpty, 14, secondary: true));
            return;
        }

        foreach (var (id, name, settings) in current)
        {
            _sections.Children.Add(CreateSection(id, name, settings));
        }
    }

    private UIElement CreateSection(string id, string name, ISettingsRegistry settings)
    {
        var title = UiTheme.Label(name, 16, semibold: true);
        title.VerticalAlignment = VerticalAlignment.Center;

        var link = new Button { Content = UiStrings.ModulePageLink, Padding = new Thickness(12, 4, 12, 4) };
        link.Click += (_, _) => _openModulePage(id);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        Grid.SetColumn(link, 1);
        header.Children.Add(link);

        var body = new StackPanel();
        body.Children.Add(header);
        if (settings.Definitions.Count == 0)
        {
            body.Children.Add(UiTheme.Label(UiStrings.ModuleHasNoSettings, 13, secondary: true));
        }
        else
        {
            body.Children.Add(new SettingsView(settings, id, _actions, compact: false));
        }

        return UiTheme.Card(body, new Thickness(0, 0, 0, 12));
    }
}
