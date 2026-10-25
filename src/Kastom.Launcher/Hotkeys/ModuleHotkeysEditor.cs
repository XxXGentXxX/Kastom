using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kastom.Core.Abstractions;
using Kastom.Core.Hotkeys;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Hotkeys;

/// <summary>
/// Редактор одного розділу клавіш (лаунчер чи модуль, Г2): адреса розділу списком і дії розділу, у кожної своя клавіша.
/// Поєднання дії = адреса + її клавіша. Один елемент і на сторінці «Гарячі клавіші», і на сторінці модуля.
/// </summary>
public sealed class ModuleHotkeysEditor : UserControl
{
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

    private readonly IHotkeyManager _hotkeys;
    private readonly string _sectionId;
    private readonly Func<HotkeyBindingInfo, bool>? _filter;
    private readonly Dictionary<string, string> _errors = new();
    private readonly StackPanel _root = new();
    private string? _addressError;
    private string? _capturingId;
    private IDisposable? _suspension;
    private Window? _hostWindow;
    private bool _updating;
    private bool _refreshQueued;

    /// <param name="filter">Показати лише підхожі дії (фільтр за клавішею на схемі клавіатури); адреса видна завжди.</param>
    public ModuleHotkeysEditor(IHotkeyManager hotkeys, string sectionId, Func<HotkeyBindingInfo, bool>? filter = null)
    {
        _hotkeys = hotkeys;
        _sectionId = sectionId;
        _filter = filter;

        Grid.SetIsSharedSizeScope(_root, true);
        Content = _root;
        Focusable = true;
        FocusVisualStyle = null;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Build();
    }

    public string SectionId => _sectionId;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hotkeys.Changed -= OnHotkeysChanged;
        _hotkeys.Changed += OnHotkeysChanged;

        // Користувач натиснув «Змінити» й перейшов в інше вікно: захоплення скасовуємо, щоб клавіші не лишалися вимкненими.
        if (_hostWindow is not null) _hostWindow.Deactivated -= OnHostWindowDeactivated;
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null) _hostWindow.Deactivated += OnHostWindowDeactivated;
        Build();
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
        Build();
    }

    private void OnHotkeysChanged(object? sender, EventArgs e) => QueueBuild();

    // Перебудова відкладається: список адрес не можна замінювати всередині його власної події вибору.
    private void QueueBuild()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            Build();
        }));
    }

    private void Build()
    {
        _root.Children.Clear();
        var section = _hotkeys.Sections.FirstOrDefault(x => x.Id == _sectionId);
        if (section is null) return;

        var bindings = _hotkeys.Bindings;
        _root.Children.Add(CreateAddressRow(section));
        foreach (var note in CreateAddressNotes(section)) _root.Children.Add(note);

        foreach (var binding in bindings.Where(b => b.Action.ModuleId == _sectionId && (_filter?.Invoke(b) ?? true)))
        {
            _root.Children.Add(CreateRow(binding, bindings));
        }

        // Перебудова прибирає кнопку з фокусом; без фокуса на редакторі клавіша не дійде до захоплення.
        if (_capturingId is not null) Focus();
    }

    // ---------- Адреса ----------

    private static string AddressLabel(HotkeyModifiers address)
    {
        var text = HotkeyAddress.Format(address);
        if (HotkeyAddress.IsCtrlAlt(address)) return text + HotkeyStrings.AddressAltGrSuffix;
        return HotkeyAddress.IsSingle(address) ? text + HotkeyStrings.AddressCautionSuffix : text;
    }

    private UIElement CreateAddressRow(HotkeySectionInfo section)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = HotkeyStrings.AddressLabel,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        grid.Children.Add(label);

        var presets = HotkeyAddress.Presets.ToList();
        if (!presets.Contains(section.Address)) presets.Add(section.Address);

        var combo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(combo, HotkeyStrings.AddressLabel);
        foreach (var preset in presets)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = AddressLabel(preset),
                Tag = preset,
                ToolTip = HotkeyAddress.IsCtrlAlt(preset) ? HotkeyStrings.AddressCtrlAltNote
                    : HotkeyAddress.IsSingle(preset) ? HotkeyStrings.AddressSingleWarning : null,
            });
        }

        _updating = true;
        combo.SelectedIndex = presets.IndexOf(section.Address);
        _updating = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (_updating || combo.SelectedItem is not ComboBoxItem { Tag: HotkeyModifiers address }) return;
            _addressError = null;
            var result = _hotkeys.SetAddress(_sectionId, address);
            if (!result.Success) _addressError = result.Error;
            QueueBuild();
        };
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);

        var reset = new Button { Content = HotkeyStrings.AddressResetButton, IsEnabled = !section.IsDefaultAddress };
        reset.Click += (_, _) =>
        {
            _addressError = null;
            var result = _hotkeys.ResetAddress(_sectionId);
            if (!result.Success) _addressError = result.Error;
            QueueBuild();
        };
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);
        return grid;
    }

    private IEnumerable<UIElement> CreateAddressNotes(HotkeySectionInfo section)
    {
        yield return CreateNote(HotkeyStrings.AddressHint, null);

        if (HotkeyAddress.IsCtrlAlt(section.Address)) yield return CreateNote(HotkeyStrings.AddressCtrlAltNote, null);
        else if (HotkeyAddress.IsSingle(section.Address)) yield return CreateNote(HotkeyStrings.AddressSingleWarning, WarningBrush);

        if (_addressError is not null) yield return CreateNote(_addressError, ErrorBrush);
    }

    // ---------- Дії ----------

    private FrameworkElement CreateRow(HotkeyBindingInfo binding, IReadOnlyList<HotkeyBindingInfo> all)
    {
        var id = binding.Action.GlobalId;
        var capturing = id == _capturingId;

        // Колонки жесту й кнопок спільні для всіх рядків (SharedSizeGroup), назва забирає решту ширини;
        // статус стоїть під назвою, тож вузьке вікно не обрізає кнопки.
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
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
            TextWrapping = TextWrapping.Wrap,
            Opacity = binding.Status is HotkeyBindingStatus.Unassigned or HotkeyBindingStatus.Disabled ? 0.6 : 0.85,
        };
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
            Text = capturing ? HotkeyStrings.CaptureKeyPrompt : binding.Gesture?.ToString() ?? "—",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontStyle = capturing ? FontStyles.Italic : FontStyles.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = binding.Gesture is null && !capturing ? 0.5 : 1,
        };
        Grid.SetColumn(gesture, 1);
        grid.Children.Add(gesture);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var change = new Button
        {
            Content = capturing ? HotkeyStrings.CancelButton : HotkeyStrings.ChangeButton,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        change.Click += (_, _) => ToggleCapture(id);
        var clear = new Button
        {
            Content = HotkeyStrings.ClearButton,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = binding.Gesture is not null && !capturing,
        };
        clear.Click += (_, _) =>
        {
            EndCapture();
            SetResult(id, _hotkeys.SetKey(id, null));
        };
        var reset = new Button
        {
            Content = HotkeyStrings.ResetButton,
            MinWidth = 80,
            IsEnabled = binding.Action.Action.DefaultHotkey is not null,
        };
        reset.Click += (_, _) =>
        {
            EndCapture();
            SetResult(id, _hotkeys.ResetKey(id));
        };
        buttons.Children.Add(change);
        buttons.Children.Add(clear);
        buttons.Children.Add(reset);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var container = new StackPanel();
        container.Children.Add(grid);
        if (capturing)
        {
            container.Children.Add(CreateNote(HotkeyStrings.CaptureKeyHint, null));
        }
        else if (_errors.TryGetValue(id, out var error))
        {
            container.Children.Add(CreateNote(error, ErrorBrush));
        }

        return container;
    }

    private static TextBlock CreateNote(string text, Brush? foreground)
    {
        var note = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        if (foreground is null) note.Opacity = 0.7;
        else note.Foreground = foreground;
        return note;
    }

    // ---------- Захоплення однієї клавіші ----------

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

        Build();
    }

    private void EndCapture()
    {
        _capturingId = null;
        _suspension?.Dispose();
        _suspension = null;
    }

    private void SetResult(string id, HotkeyAssignResult result)
    {
        if (result.Success || string.IsNullOrEmpty(result.Error)) _errors.Remove(id);
        else _errors[id] = result.Error;
        Build();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
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
            return; // чекаємо саму клавішу: модифікатори дає адреса розділу
        }

        if (key == Key.Escape)
        {
            EndCapture();
            Build();
            return;
        }

        EndCapture(); // реєстрації повертаються до перевірки нового поєднання
        SetResult(id, _hotkeys.SetKey(id, KeyInterop.VirtualKeyFromKey(key)));
    }
}
