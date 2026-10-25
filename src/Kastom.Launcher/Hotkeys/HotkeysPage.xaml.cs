using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kastom.Core.Abstractions;

namespace Kastom.Launcher.Hotkeys;

/// <summary>Сторінка «Гарячі клавіші»: візуалізація клавіатури й редактор кожного розділу (лаунчер, модулі).</summary>
public partial class HotkeysPage : UserControl
{
    private readonly IHotkeyManager _hotkeys;
    private bool _updating;
    private bool _refreshQueued;
    private string? _signature;

    public HotkeysPage(IHotkeyManager hotkeys, IActionCatalog actions)
    {
        _hotkeys = hotkeys;
        _ = actions; // Дії беремо з hotkeys.Bindings: там по запису на кожну.
        InitializeComponent();

        KeyMap.SelectedKeyChanged += (_, _) => Refresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hotkeys.Changed -= OnHotkeysChanged;
        _hotkeys.Changed += OnHotkeysChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _hotkeys.Changed -= OnHotkeysChanged;

    private void OnHotkeysChanged(object? sender, EventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            Refresh();
        }));
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _hotkeys.Enabled = EnabledCheck.IsChecked == true;
    }

    private void OnFilterClear(object sender, RoutedEventArgs e) => KeyMap.SelectedKey = null;

    private void Refresh()
    {
        var bindings = _hotkeys.Bindings;

        _updating = true;
        EnabledCheck.IsChecked = _hotkeys.Enabled;
        _updating = false;

        KeyMap.Update(bindings);

        var filter = KeyMap.SelectedKey;
        FilterBar.Visibility = filter is null ? Visibility.Collapsed : Visibility.Visible;
        if (filter is not null)
            FilterText.Text = string.Format(CultureInfo.CurrentCulture, HotkeyStrings.FilterInfo, filter.Label);

        Func<HotkeyBindingInfo, bool> matches = filter is null
            ? _ => true
            : b => b.Gesture is { } g && KeyboardLayout.Uses(g, filter);
        var visible = bindings.Where(matches).ToList();

        // Редактори самі оновлюють свої значення; сторінка будує їх наново, лише коли змінився склад розділів чи фільтр.
        var signature = string.Join(
            "|",
            filter?.Label,
            filter?.VirtualKey,
            string.Join(";", visible.GroupBy(b => b.Action.ModuleId).Select(g => $"{g.Key}:{string.Join(",", g.Select(b => b.Action.GlobalId))}")));
        if (signature != _signature)
        {
            _signature = signature;
            Sections.Children.Clear();
            foreach (var group in visible.GroupBy(b => b.Action.ModuleId))
            {
                Sections.Children.Add(CreateHeader(group.First()));
                Sections.Children.Add(new ModuleHotkeysEditor(_hotkeys, group.Key, matches));
            }
        }

        if (bindings.Count == 0)
            ShowEmpty(HotkeyStrings.NoActions);
        else if (visible.Count == 0)
            ShowEmpty(HotkeyStrings.NoBindingsForKey);
        else
            EmptyText.Visibility = Visibility.Collapsed;
    }

    private void ShowEmpty(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    private static FrameworkElement CreateHeader(HotkeyBindingInfo first)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 6) };
        panel.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(HotkeyPresenter.GroupColor(first.Action.ModuleId)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = HotkeyPresenter.ModuleTitle(first.Action),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        return panel;
    }
}
