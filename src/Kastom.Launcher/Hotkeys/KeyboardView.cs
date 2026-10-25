using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kastom.Core.Abstractions;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Launcher.Hotkeys;

/// <summary>
/// Схема клавіатури: клавіші, задіяні в поєднаннях, підсвічені кольором групи (лаунчер або модуль);
/// клавіші з конфліктом мають помаранчеву рамку. Клац по клавіші вмикає фільтр, повторний — вимикає.
/// </summary>
public sealed class KeyboardView : UserControl
{
    private const double Unit = 40;
    private const double Gap = 2;

    private static readonly SolidColorBrush ConflictBrush = Frozen(Color.FromRgb(0xF2, 0xA1, 0x00));
    private static readonly SolidColorBrush ModifierBrush = Frozen(Color.FromRgb(0x5B, 0x6E, 0x7C));
    private static readonly SolidColorBrush OnColorBrush = Frozen(Colors.White);

    private readonly Dictionary<KeyboardKey, Border> _borders = new();
    private KeyboardKey? _selected;

    public KeyboardView()
    {
        var canvas = new Canvas
        {
            Width = KeyboardLayout.TotalWidth * Unit,
            Height = KeyboardLayout.TotalHeight * Unit,
        };

        foreach (var key in KeyboardLayout.Keys)
        {
            var border = CreateKey(key);
            _borders[key] = border;
            canvas.Children.Add(border);
        }

        Content = new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Loaded += (_, _) => ApplyThemeFallback();
    }

    /// <summary>Клавіша, за якою зараз фільтрується список; null — фільтра немає.</summary>
    public KeyboardKey? SelectedKey
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Refresh();
            SelectedKeyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedKeyChanged;

    private IReadOnlyList<HotkeyBindingInfo> _bindings = [];

    public void Update(IReadOnlyList<HotkeyBindingInfo> bindings)
    {
        _bindings = bindings;
        Refresh();
    }

    private static bool IsShown(HotkeyBindingInfo b) =>
        b.Gesture is not null
        && b.Status is HotkeyBindingStatus.Active or HotkeyBindingStatus.ConflictInternal or HotkeyBindingStatus.TakenBySystem;

    private void Refresh()
    {
        var shown = _bindings.Where(IsShown).ToList();
        foreach (var (key, border) in _borders)
        {
            var users = shown.Where(b => KeyboardLayout.Uses(b.Gesture!.Value, key)).ToList();
            ApplyStyle(key, border, users);
        }
    }

    private void ApplyStyle(KeyboardKey key, Border border, List<HotkeyBindingInfo> users)
    {
        var text = (TextBlock)border.Child;
        var selected = key == _selected;

        if (users.Count == 0)
        {
            border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            border.ToolTip = null;
        }
        else
        {
            var groups = users.Select(b => b.Action.ModuleId).Distinct().ToList();
            border.ClearValue(Border.BackgroundProperty);
            border.Background = key.Modifier != HotkeyModifiers.None && groups.Count > 1
                ? ModifierBrush
                : Frozen(HotkeyPresenter.GroupColor(groups[0]));
            text.ClearValue(TextBlock.ForegroundProperty);
            text.Foreground = OnColorBrush;
            border.ToolTip = string.Join(
                Environment.NewLine,
                users.Select(b => string.Format(
                    CultureInfo.CurrentCulture,
                    HotkeyStrings.TooltipLine,
                    b.Gesture,
                    b.Action.Action.Title,
                    HotkeyPresenter.ModuleTitle(b.Action)) + Suffix(b)));
        }

        var conflict = users.Any(b => b.Status is HotkeyBindingStatus.ConflictInternal or HotkeyBindingStatus.TakenBySystem);
        if (selected)
        {
            border.BorderThickness = new Thickness(3);
            border.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
        }
        else if (conflict)
        {
            border.BorderThickness = new Thickness(2.5);
            border.BorderBrush = ConflictBrush;
        }
        else
        {
            border.BorderThickness = new Thickness(1);
            border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        }
    }

    private string Suffix(HotkeyBindingInfo binding) =>
        binding.Status == HotkeyBindingStatus.Active ? "" : " — " + HotkeyPresenter.StatusText(binding, _bindings);

    private Border CreateKey(KeyboardKey key)
    {
        var text = new TextBlock
        {
            Text = key.Label,
            FontSize = key.Label.Length >= 4 && key.Width < 1.5 ? 11 : 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var border = new Border
        {
            Width = key.Width * Unit - Gap,
            Height = key.Height * Unit - Gap,
            CornerRadius = new CornerRadius(5),
            Child = text,
            Cursor = Cursors.Hand,
            // Без фону Border не ловить клацання по порожніх місцях клавіші.
            Background = Brushes.Transparent,
        };
        Canvas.SetLeft(border, key.X * Unit + Gap / 2);
        Canvas.SetTop(border, key.Y * Unit + Gap / 2);
        border.MouseLeftButtonUp += (_, e) =>
        {
            SelectedKey = _selected == key ? null : key;
            e.Handled = true;
        };
        return border;
    }

    // Якщо Fluent-ресурсів немає (тема не ввімкнена), беремо системні кольори.
    private void ApplyThemeFallback()
    {
        if (TryFindResource("ControlFillColorDefaultBrush") is not null) return;
        Resources["ControlFillColorDefaultBrush"] = SystemColors.ControlBrush;
        Resources["ControlStrokeColorDefaultBrush"] = SystemColors.ControlDarkBrush;
        Resources["TextFillColorPrimaryBrush"] = SystemColors.ControlTextBrush;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
