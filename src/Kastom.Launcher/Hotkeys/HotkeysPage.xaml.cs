using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kastom.Core.Abstractions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Hotkeys;

/// <summary>Сторінка «Гарячі клавіші»: візуалізація клавіатури й поєднання за розділами.</summary>
public partial class HotkeysPage : UserControl
{
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

    private readonly IHotkeyManager _hotkeys;
    private readonly Dictionary<string, string> _errors = new();
    private string? _capturingId;
    private IDisposable? _suspension;
    private Window? _hostWindow;
    private bool _updating;
    private bool _refreshQueued;

    public HotkeysPage(IHotkeyManager hotkeys, IActionCatalog actions)
    {
        _hotkeys = hotkeys;
        _ = actions; // Дії беремо з hotkeys.Bindings: там по запису на кожну.
        InitializeComponent();

        KeyMap.SelectedKeyChanged += (_, _) => Refresh();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hotkeys.Changed -= OnHotkeysChanged;
        _hotkeys.Changed += OnHotkeysChanged;

        // Користувач натиснув «Змінити» й перейшов в інше вікно: захоплення скасовуємо, щоб клавіші не лишалися вимкненими.
        if (_hostWindow is not null) _hostWindow.Deactivated -= OnHostWindowDeactivated;
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null) _hostWindow.Deactivated += OnHostWindowDeactivated;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hotkeys.Changed -= OnHotkeysChanged;
        if (_hostWindow is not null) _hostWindow.Deactivated -= OnHostWindowDeactivated;
        _hostWindow = null;
        EndCapture();
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e)
    {
        if (_capturingId is null) return;
        EndCapture();
        Refresh();
    }

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

        var visible = filter is null
            ? bindings
            : bindings.Where(b => b.Gesture is { } g && KeyboardLayout.Uses(g, filter)).ToList();

        Sections.Children.Clear();
        foreach (var group in visible.GroupBy(b => b.Action.ModuleId))
        {
            Sections.Children.Add(CreateHeader(group.First()));
            foreach (var binding in group) Sections.Children.Add(CreateRow(binding, bindings));
        }

        if (bindings.Count == 0)
            ShowEmpty(HotkeyStrings.NoActions);
        else if (visible.Count == 0)
            ShowEmpty(HotkeyStrings.NoBindingsForKey);
        else
            EmptyText.Visibility = Visibility.Collapsed;

        // Перебудова прибирає кнопку з фокусом; без фокуса на сторінці клавіші не дійдуть до захоплення.
        if (_capturingId is not null) Focus();
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

    private FrameworkElement CreateRow(HotkeyBindingInfo binding, IReadOnlyList<HotkeyBindingInfo> all)
    {
        var id = binding.Action.GlobalId;
        var capturing = id == _capturingId;

        // Колонки жесту й кнопок спільні для всіх рядків (SharedSizeGroup), назва забирає решту ширини;
        // статус стоїть під назвою, тож вузьке вікно не обрізає кнопки.
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Gesture", MinWidth = 130 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Buttons" });

        var titleBlock = new TextBlock
        {
            Text = binding.Action.Action.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = string.IsNullOrWhiteSpace(binding.Action.Action.Description)
                ? binding.Action.Action.Title
                : binding.Action.Action.Description,
        };
        var status = new TextBlock
        {
            Text = HotkeyPresenter.StatusText(binding, all),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = binding.Status is HotkeyBindingStatus.Unassigned or HotkeyBindingStatus.Disabled ? 0.6 : 0.85,
        };
        status.ToolTip = status.Text;
        if (binding.Status is HotkeyBindingStatus.ConflictInternal or HotkeyBindingStatus.TakenBySystem)
        {
            status.Foreground = WarningBrush;
            status.Opacity = 1;
        }

        var titleCell = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        titleCell.Children.Add(titleBlock);
        titleCell.Children.Add(status);
        grid.Children.Add(titleCell);

        var gesture = new TextBlock
        {
            Text = capturing ? HotkeyStrings.CapturePrompt : binding.Gesture?.ToString() ?? "—",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontStyle = capturing ? FontStyles.Italic : FontStyles.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = binding.Gesture is null && !capturing ? 0.5 : 1,
        };
        Grid.SetColumn(gesture, 1);
        grid.Children.Add(gesture);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var change = new Button
        {
            Content = capturing ? HotkeyStrings.CancelButton : HotkeyStrings.ChangeButton,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        change.Click += (_, _) => ToggleCapture(id);
        var reset = new Button { Content = HotkeyStrings.ResetButton, MinWidth = 80 };
        reset.Click += (_, _) => Reset(id);
        buttons.Children.Add(change);
        buttons.Children.Add(reset);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var container = new StackPanel();
        container.Children.Add(grid);
        if (capturing)
        {
            container.Children.Add(CreateNote(HotkeyStrings.CaptureHint, null));
        }
        else if (_errors.TryGetValue(id, out var error))
        {
            container.Children.Add(CreateNote(error, ErrorBrush));
        }

        return container;
    }

    private static TextBlock CreateNote(string text, Brush? foreground)
    {
        var note = new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        if (foreground is null) note.Opacity = 0.7;
        else note.Foreground = foreground;
        return note;
    }

    private void ToggleCapture(string id)
    {
        _errors.Remove(id);
        if (_capturingId == id)
        {
            EndCapture();
        }
        else
        {
            // Поки йде захоплення, поєднання знято з реєстрації: інакше Windows перехопить натискання раніше за вікно.
            _suspension ??= _hotkeys.Suspend();
            _capturingId = id;
        }

        Refresh();
    }

    private void EndCapture()
    {
        _capturingId = null;
        _suspension?.Dispose();
        _suspension = null;
    }

    private void Reset(string id)
    {
        EndCapture();
        var result = _hotkeys.ResetToDefault(id);
        SetResult(id, result);
    }

    private void SetResult(string id, HotkeyAssignResult result)
    {
        if (result.Success || string.IsNullOrEmpty(result.Error)) _errors.Remove(id);
        else _errors[id] = result.Error;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingId is not { } id) return;

        // Alt-поєднання приходять як Key.System, справжня клавіша лежить у SystemKey.
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // чекаємо основну клавішу
        }

        var modifiers = (HotkeyModifiers)(int)Keyboard.Modifiers;
        if (modifiers == HotkeyModifiers.None && key == Key.Escape)
        {
            EndCapture();
            Refresh();
            return;
        }

        EndCapture(); // реєстрації повертаються до перевірки нового поєднання
        if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete)
        {
            SetResult(id, _hotkeys.Assign(id, null));
            return;
        }

        var gesture = new HotkeyGesture(modifiers, KeyInterop.VirtualKeyFromKey(key));
        SetResult(id, _hotkeys.Assign(id, gesture));
    }
}
