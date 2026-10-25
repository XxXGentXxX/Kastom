using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;
using Kastom.Sdk.Settings;

namespace Kastom.Launcher.Widgets;

/// <summary>Перелік встановлених модулів: увімкнути/вимкнути, швидкі налаштування, перехід у лаунчер.</summary>
public sealed class ModulesQuickView : UserControl
{
    private readonly IModuleManager _modules;
    private readonly IActionCatalog _actions;
    private readonly Action<string?> _openMainWindow;
    private readonly StackPanel _list = new();
    private readonly TextBlock _empty;
    private readonly Dictionary<string, ModuleRow> _rows = new();

    /// <param name="openMainWindow">Відкрити головне вікно; аргумент — id модуля або null.</param>
    public ModulesQuickView(IModuleManager modules, IActionCatalog actions, Action<string?> openMainWindow)
    {
        _modules = modules;
        _actions = actions;
        _openMainWindow = openMainWindow;

        _empty = UiTheme.Label(UiStrings.QuickEmpty, 13, secondary: true);
        _empty.Margin = new Thickness(4, 12, 4, 12);
        _empty.TextAlignment = TextAlignment.Center;

        var body = new StackPanel();
        body.Children.Add(_empty);
        body.Children.Add(_list);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        // У швидкій панелі цей список лежить усередині іншого ScrollViewer: передаємо йому колесо, коли свого прокручування немає.
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (scroll.ScrollableHeight > 0 || scroll.Parent is not UIElement parent)
            {
                return;
            }

            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = scroll });
        };

        var open = new Button { Content = UiStrings.OpenLauncher, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        open.Click += (_, _) => _openMainWindow(null);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(scroll);
        Grid.SetRow(open, 1);
        layout.Children.Add(open);
        Content = layout;

        Refresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _modules.Changed -= OnModulesChanged;
        _modules.Changed += OnModulesChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _modules.Changed -= OnModulesChanged;

    private void OnModulesChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.BeginInvoke(Refresh);
        }
    }

    private void Refresh()
    {
        var infos = _modules.Modules.Where(m => m.IsInstalled).ToList();

        foreach (var id in _rows.Keys.Where(id => infos.All(m => m.Id != id)).ToList())
        {
            _rows.Remove(id);
        }

        foreach (var info in infos)
        {
            if (!_rows.TryGetValue(info.Id, out var row))
            {
                row = new ModuleRow(this, info);
                _rows[info.Id] = row;
            }

            row.Update(info);
        }

        // Рядки живуть між оновленнями (щоб не губити розгорнуті налаштування); міняємо лише склад і порядок.
        var ordered = infos.Select(m => _rows[m.Id].Root).ToList();
        if (!_list.Children.Cast<UIElement>().SequenceEqual(ordered))
        {
            _list.Children.Clear();
            foreach (var element in ordered)
            {
                _list.Children.Add(element);
            }
        }

        _empty.Visibility = infos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class ModuleRow
    {
        private readonly ModulesQuickView _owner;
        private readonly string _moduleId;
        private readonly TextBlock _name;
        private readonly TextBlock _status;
        private readonly TextBlock _note;
        private readonly TextBlock _error;
        private readonly ToggleButton _switch;
        private readonly Expander _expander;
        private ModuleInfo _info;
        private ISettingsRegistry? _settingsShown;
        private bool _busy;
        private string? _operationError;
        private bool _restartPending;

        public ModuleRow(ModulesQuickView owner, ModuleInfo info)
        {
            _owner = owner;
            _moduleId = info.Id;
            _info = info;

            _name = UiTheme.Label(info.Name, 14, semibold: true);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.TextWrapping = TextWrapping.NoWrap;
            _status = UiTheme.Label("", 12, secondary: true);
            _note = UiTheme.Label(UiStrings.QuickRestartNote, 12, secondary: true);
            _note.Visibility = Visibility.Collapsed;
            _error = UiTheme.ErrorLabel();

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(_name);
            texts.Children.Add(_status);
            texts.Children.Add(_note);
            texts.Children.Add(_error);

            var settingsButton = new Button
            {
                Content = UiStrings.QuickSettings,
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(8, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            settingsButton.Click += (_, _) => _owner._openMainWindow(_moduleId);

            _switch = new ToggleButton { Style = UiTheme.SwitchStyle };
            AutomationProperties.SetName(_switch, info.Name);
            _switch.Click += OnSwitchClick;

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(texts);
            Grid.SetColumn(settingsButton, 1);
            top.Children.Add(settingsButton);
            Grid.SetColumn(_switch, 2);
            top.Children.Add(_switch);

            _expander = new Expander
            {
                Header = UiStrings.QuickQuickSettings,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var panel = new StackPanel();
            panel.Children.Add(top);
            panel.Children.Add(_expander);

            var card = UiTheme.Card(panel, new Thickness(0, 0, 0, 6));
            card.Padding = new Thickness(12, 10, 12, 10);
            Root = card;
        }

        public FrameworkElement Root { get; }

        public void Update(ModuleInfo info)
        {
            _info = info;
            _name.Text = info.Name;
            ApplyState();
        }

        private void ApplyState()
        {
            var info = _info;
            var status = !info.IsEnabled
                ? UiStrings.QuickStatusDisabled
                : info.IsRunning ? UiStrings.QuickStatusRunning : UiStrings.QuickStatusNotRunning;
            _status.Text = string.IsNullOrEmpty(info.InstalledVersion) ? status : $"{status} · {info.InstalledVersion}";

            _note.Visibility = info.RestartRequired || _restartPending ? Visibility.Visible : Visibility.Collapsed;
            UiTheme.ShowError(_error, _operationError ?? info.Error);

            _switch.IsEnabled = !_busy;
            if (!_busy)
            {
                _switch.IsChecked = info.IsEnabled;
            }

            ShowQuickSettings();
        }

        private void ShowQuickSettings()
        {
            var settings = _info.IsRunning ? _owner._modules.GetSettings(_moduleId) : null;
            if (settings is null || !settings.Definitions.Any(d => d.IsQuick))
            {
                _expander.Visibility = Visibility.Collapsed;
                _expander.Content = null;
                _settingsShown = null;
                return;
            }

            if (!ReferenceEquals(_settingsShown, settings))
            {
                _expander.Content = new SettingsView(settings, _moduleId, _owner._actions, compact: true) { Margin = new Thickness(0, 4, 0, 0) };
                _settingsShown = settings;
            }

            _expander.Visibility = Visibility.Visible;
        }

        private async void OnSwitchClick(object sender, RoutedEventArgs e)
        {
            var enable = _switch.IsChecked == true;
            _busy = true;
            _operationError = null;
            _switch.IsEnabled = false;
            try
            {
                var result = enable
                    ? await _owner._modules.EnableAsync(_moduleId)
                    : await _owner._modules.DisableAsync(_moduleId);
                if (!result.Success)
                {
                    _operationError = string.Format(UiStrings.QuickToggleFailedFormat, result.Error);
                }
                else if (result.RestartRequired)
                {
                    _restartPending = true;
                }
            }
            catch (Exception ex)
            {
                _operationError = string.Format(UiStrings.QuickToggleFailedFormat, UiTheme.FormatError(ex));
            }
            finally
            {
                _busy = false;
                ApplyState();
            }
        }
    }
}
