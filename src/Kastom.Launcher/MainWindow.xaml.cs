using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Hotkeys;
using Kastom.Launcher.Pages;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Services;
using Kastom.Launcher.Shell;

namespace Kastom.Launcher;

public enum NavPage
{
    Modules,
    ModuleSettings,
    Hotkeys,
    Launcher,
}

/// <summary>Пункт лівої навігації: сторінка, заголовок розділу або увімкнений модуль.</summary>
public sealed class NavItem
{
    public required string Title { get; init; }

    public bool IsHeader { get; init; }

    public NavPage? Page { get; init; }

    /// <summary>Id модуля для пунктів «Увімкнені модулі».</summary>
    public string? ModuleId { get; init; }
}

/// <summary>
/// Головне вікно. Створюється на вимогу й після закриття не лишає посилань на себе:
/// сторінки відписуються від подій служб, App забуває вікно.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LauncherServices _services;
    private readonly Action _restartLauncher;
    private readonly ObservableCollection<NavItem> _items = [];
    private readonly NavItem _modulesHeader = new() { Title = Strings.NavEnabledModules, IsHeader = true };
    private bool _closed;

    public MainWindow(LauncherServices services, Action restartLauncher)
    {
        _services = services;
        _restartLauncher = restartLauncher;
        InitializeComponent();

        try
        {
            Icon = AppIcon.CreateImageSource(32);
        }
        catch (Exception exception)
        {
            services.Log.Warn($"Не вдалося намалювати значок вікна: {exception.Message}");
        }

        _items.Add(new NavItem { Title = Strings.NavModules, Page = NavPage.Modules });
        _items.Add(new NavItem { Title = Strings.NavModuleSettings, Page = NavPage.ModuleSettings });
        _items.Add(new NavItem { Title = Strings.NavHotkeys, Page = NavPage.Hotkeys });
        _items.Add(new NavItem { Title = Strings.NavLauncher, Page = NavPage.Launcher });
        _items.Add(_modulesHeader);
        Nav.ItemsSource = _items;

        RebuildModuleItems();
        Nav.SelectedIndex = 0;

        _services.Modules.Changed += OnModulesChanged;
        Closed += OnClosed;
    }

    /// <summary>Відкрити сторінку з лівої навігації.</summary>
    public void NavigateTo(NavPage page)
    {
        var item = _items.FirstOrDefault(i => i.Page == page);
        if (item is not null) Nav.SelectedItem = item;
    }

    /// <summary>Відкрити сторінку модуля (навіть якщо його немає серед увімкнених у навігації).</summary>
    public void OpenModulePage(string moduleId)
    {
        var item = _items.FirstOrDefault(i => i.ModuleId == moduleId);
        if (item is not null)
        {
            Nav.SelectedItem = item;
            return;
        }

        Nav.SelectedItem = null;
        ShowPage(CreateModulePage(moduleId));
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem { IsHeader: false } item) return;
        ShowPage(item.ModuleId is { } moduleId ? CreateModulePage(moduleId) : CreatePage(item.Page ?? NavPage.Modules));
    }

    private void ShowPage(UIElement page)
    {
        var old = PageHost.Content;
        PageHost.Content = page;
        (old as IDisposable)?.Dispose();
    }

    private UIElement CreatePage(NavPage page)
    {
        try
        {
            switch (page)
            {
                case NavPage.Modules:
                    return new ModulesPage(_services, Confirm, _restartLauncher);
                case NavPage.ModuleSettings:
                    return new ModuleSettingsAggregatePage(_services.Modules, _services.Actions, OpenModulePage);
                case NavPage.Hotkeys:
                    return _services.Hotkeys is { } hotkeys
                        ? new HotkeysPage(hotkeys, _services.Actions)
                        : Unavailable("менеджер клавіш не запущено");
                default:
                    return new LauncherSettingsPage(_services);
            }
        }
        catch (Exception exception)
        {
            _services.Log.Error($"Не вдалося створити сторінку {page}", exception);
            return Unavailable(exception.Message);
        }
    }

    private UIElement CreateModulePage(string moduleId)
    {
        try
        {
            return new ModulePage(moduleId, _services.Modules, _services.Actions, _services.Widgets);
        }
        catch (Exception exception)
        {
            _services.Log.Error($"Не вдалося створити сторінку модуля {moduleId}", exception);
            return Unavailable(exception.Message);
        }
    }

    private static UIElement Unavailable(string reason) => new TextBlock
    {
        Text = string.Format(Strings.PageUnavailable, reason),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(24),
    };

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, Strings.ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // ---------- Увімкнені модулі в навігації ----------

    private void OnModulesChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!_closed) RebuildModuleItems();
        });

    private void RebuildModuleItems()
    {
        List<ModuleInfo> enabled;
        try
        {
            enabled = _services.Modules.Modules.Where(module => module.IsEnabled).ToList();
        }
        catch (Exception exception)
        {
            _services.Log.Error("Не вдалося отримати перелік увімкнених модулів", exception);
            enabled = [];
        }

        var current = _items.Where(i => i.ModuleId is not null).ToList();
        var desired = new List<NavItem>();
        foreach (var module in enabled)
        {
            var existing = current.FirstOrDefault(i => i.ModuleId == module.Id && i.Title == module.Name);
            desired.Add(existing ?? new NavItem { Title = module.Name, ModuleId = module.Id });
        }

        var selectedGone = Nav.SelectedItem is NavItem { ModuleId: not null } selected && !desired.Contains(selected);
        foreach (var gone in current.Where(i => !desired.Contains(i))) _items.Remove(gone);

        var firstModuleIndex = _items.IndexOf(_modulesHeader) + 1;
        for (var i = 0; i < desired.Count; i++)
        {
            var index = _items.IndexOf(desired[i]);
            if (index < 0) _items.Insert(firstModuleIndex + i, desired[i]);
            else if (index != firstModuleIndex + i) _items.Move(index, firstModuleIndex + i);
        }

        if (selectedGone) NavigateTo(NavPage.Modules);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _services.Modules.Changed -= OnModulesChanged;
        Closed -= OnClosed;

        var page = PageHost.Content;
        PageHost.Content = null;
        (page as IDisposable)?.Dispose();

        Nav.ItemsSource = null;
        Icon = null;
        if (ReferenceEquals(Application.Current?.MainWindow, this)) Application.Current.MainWindow = null;
    }
}
