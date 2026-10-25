using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Kastom.Core.Abstractions;
using Kastom.Launcher.Resources;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Settings;

namespace Kastom.Launcher.Settings;

/// <summary>Малює описи налаштувань модуля. compact = лише IsQuick, щільніше (швидка панель, віджет).</summary>
public sealed class SettingsView : UserControl
{
    private static readonly TimeSpan RangeDebounce = TimeSpan.FromMilliseconds(150);

    private readonly ISettingsRegistry _settings;
    private readonly string _moduleId;
    private readonly IActionCatalog _actions;
    private readonly bool _compact;
    private readonly List<SettingRow> _rows = new();

    public SettingsView(ISettingsRegistry settings, string moduleId, IActionCatalog actions, bool compact)
    {
        _settings = settings;
        _moduleId = moduleId;
        _actions = actions;
        _compact = compact;

        Content = Build();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Чи є що показувати (у compact — чи є швидкі налаштування).</summary>
    public bool HasSettings => _rows.Count > 0;

    private Thickness RowMargin => _compact ? new Thickness(0, 3, 0, 3) : new Thickness(0, 6, 0, 6);

    private UIElement Build()
    {
        var root = new StackPanel();
        var definitions = _settings.Definitions.Where(d => !_compact || d.IsQuick).ToList();

        // Загальна група (Group = null) першою, далі групи в порядку першої появи.
        var groups = definitions.GroupBy(d => d.Group).OrderBy(g => g.Key is null ? 0 : 1).ToList();
        var firstBlock = true;
        foreach (var group in groups)
        {
            if (group.Key is not null && !_compact)
            {
                var header = UiTheme.Label(group.Key, 13, secondary: true, semibold: true);
                header.Margin = new Thickness(0, firstBlock ? 0 : 16, 0, 2);
                root.Children.Add(header);
            }

            foreach (var definition in group)
            {
                var row = CreateRow(definition);
                _rows.Add(row);
                root.Children.Add(row.Root);
            }

            firstBlock = false;
        }

        return root;
    }

    private SettingRow CreateRow(SettingDefinition definition) => definition switch
    {
        ToggleSetting toggle => new ToggleRow(this, toggle),
        RangeSetting range => new RangeRow(this, range),
        ChoiceSetting choice => new ChoiceRow(this, choice),
        TextSetting text => new TextRow(this, text),
        ButtonSetting button => new ButtonRow(this, button),
        _ => new UnsupportedRow(this, definition),
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.Changed -= OnSettingChanged;
        _settings.Changed += OnSettingChanged;

        // Значення могли змінитися, поки елемент не був у дереві.
        foreach (var row in _rows)
        {
            row.Refresh();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _settings.Changed -= OnSettingChanged;
        foreach (var row in _rows)
        {
            row.Flush();
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSettingChanged(sender, e));
            return;
        }

        foreach (var row in _rows)
        {
            // Власний запис рядка не повертаємо йому ж — інакше повзунок смикався б під рукою.
            if (row.Definition.Key == e.Key && !row.Writing)
            {
                row.Refresh();
            }
        }
    }

    private abstract class SettingRow
    {
        protected SettingRow(SettingsView owner, SettingDefinition definition)
        {
            Owner = owner;
            Definition = definition;
            ErrorText = UiTheme.ErrorLabel();
        }

        protected SettingsView Owner { get; }

        protected TextBlock ErrorText { get; }

        protected ISettingsRegistry Settings => Owner._settings;

        protected bool Compact => Owner._compact;

        public SettingDefinition Definition { get; }

        public FrameworkElement Root { get; protected set; } = null!;

        /// <summary>Триває власний запис у реєстр (його Changed ігнорується).</summary>
        public bool Writing { get; private set; }

        public abstract void Refresh();

        /// <summary>Дописати відкладене значення (елемент іде з дерева).</summary>
        public virtual void Flush()
        {
        }

        protected bool Write(Action write)
        {
            Writing = true;
            try
            {
                write();
                UiTheme.ShowError(ErrorText, null);
                return true;
            }
            catch (Exception ex)
            {
                UiTheme.ShowError(ErrorText, string.Format(UiStrings.SettingsSaveErrorFormat, UiTheme.FormatError(ex)));
                return false;
            }
            finally
            {
                Writing = false;
            }
        }

        protected TextBlock TitleLabel() => UiTheme.Label(Definition.Title, Compact ? 13 : 14);

        /// <summary>Назва й опис ліворуч, елемент керування праворуч.</summary>
        protected FrameworkElement SideBySide(FrameworkElement control)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(TitleLabel());
            AddDescription(left);
            grid.Children.Add(left);

            control.Margin = new Thickness(16, 0, 0, 0);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);

            return Finish(grid);
        }

        /// <summary>Назва (і необов'язковий елемент справа від неї), під нею елемент керування на всю ширину.</summary>
        protected FrameworkElement Stacked(FrameworkElement control, FrameworkElement? headerExtra = null)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(TitleLabel());
            if (headerExtra is not null)
            {
                Grid.SetColumn(headerExtra, 1);
                header.Children.Add(headerExtra);
            }

            var panel = new StackPanel();
            panel.Children.Add(header);
            AddDescription(panel);
            control.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(control);
            return Finish(panel);
        }

        protected FrameworkElement Finish(UIElement content)
        {
            var root = new StackPanel { Margin = Owner.RowMargin };
            root.Children.Add(content);
            root.Children.Add(ErrorText);
            ApplyTooltip(root);
            return root;
        }

        protected void AddDescription(Panel panel)
        {
            if (!Compact && !string.IsNullOrWhiteSpace(Definition.Description))
            {
                panel.Children.Add(UiTheme.Label(Definition.Description, 12, secondary: true));
            }
        }

        protected void ApplyTooltip(FrameworkElement element)
        {
            if (Compact && !string.IsNullOrWhiteSpace(Definition.Description))
            {
                element.ToolTip = Definition.Description;
            }
        }

        protected void SetAutomationName(DependencyObject control) => AutomationProperties.SetName(control, Definition.Title);
    }

    private sealed class ToggleRow : SettingRow
    {
        private readonly ToggleButton _toggle;

        public ToggleRow(SettingsView owner, ToggleSetting definition)
            : base(owner, definition)
        {
            _toggle = new ToggleButton { Style = UiTheme.SwitchStyle };
            SetAutomationName(_toggle);
            _toggle.IsChecked = Settings.GetBool(definition.Key);
            _toggle.Click += (_, _) =>
            {
                var value = _toggle.IsChecked == true;
                if (!Write(() => Settings.Set(definition.Key, value)))
                {
                    Refresh();
                }
            };
            Root = SideBySide(_toggle);
        }

        public override void Refresh() => _toggle.IsChecked = Settings.GetBool(Definition.Key);
    }

    private sealed class RangeRow : SettingRow
    {
        private readonly RangeSetting _range;
        private readonly Slider _slider;
        private readonly TextBlock _valueText;
        private readonly DispatcherTimer _timer;
        private double? _pending;
        private bool _updating;

        public RangeRow(SettingsView owner, RangeSetting definition)
            : base(owner, definition)
        {
            _range = definition;
            var step = definition.Step > 0 ? definition.Step : 1;
            _slider = new Slider
            {
                Minimum = definition.Min,
                Maximum = Math.Max(definition.Max, definition.Min + step),
                TickFrequency = step,
                SmallChange = step,
                LargeChange = step * 10,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetAutomationName(_slider);

            _valueText = UiTheme.Label("", Compact ? 12 : 14, secondary: true);
            _valueText.TextAlignment = TextAlignment.Right;
            _valueText.VerticalAlignment = VerticalAlignment.Center;
            _valueText.TextWrapping = TextWrapping.NoWrap;

            _timer = new DispatcherTimer(DispatcherPriority.Normal, owner.Dispatcher) { Interval = RangeDebounce };
            _timer.Tick += (_, _) => Commit();

            Load();
            _slider.ValueChanged += (_, e) =>
            {
                if (_updating)
                {
                    return;
                }

                ShowValue(e.NewValue);
                _pending = e.NewValue;
                _timer.Stop();
                _timer.Start();
            };
            _slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Commit()));

            if (Compact)
            {
                Root = Stacked(_slider, _valueText);
            }
            else
            {
                _slider.Width = 200;
                _valueText.MinWidth = 56;
                _valueText.Margin = new Thickness(12, 0, 0, 0);
                var pair = new StackPanel { Orientation = Orientation.Horizontal };
                pair.Children.Add(_slider);
                pair.Children.Add(_valueText);
                Root = SideBySide(pair);
            }
        }

        public override void Refresh()
        {
            // Користувач у процесі зміни — не перебиваємо.
            if (_pending is not null || _slider.IsMouseCaptureWithin)
            {
                return;
            }

            Load();
        }

        public override void Flush()
        {
            Commit();
            _timer.Stop();
        }

        private void Load()
        {
            _updating = true;
            try
            {
                _slider.Value = Settings.GetDouble(_range.Key);
                ShowValue(_slider.Value);
            }
            finally
            {
                _updating = false;
            }
        }

        private void Commit()
        {
            _timer.Stop();
            if (_pending is not { } value)
            {
                return;
            }

            _pending = null;
            if (!Write(() => Settings.Set(_range.Key, value)))
            {
                Load();
            }
        }

        private void ShowValue(double value)
        {
            var text = value.ToString("0.##", CultureInfo.CurrentCulture);
            if (!string.IsNullOrEmpty(_range.Unit))
            {
                text += _range.Unit == "%" ? "%" : " " + _range.Unit;
            }

            _valueText.Text = text;
        }
    }

    private sealed class ChoiceRow : SettingRow
    {
        private readonly ChoiceSetting _choice;
        private readonly ComboBox _combo;
        private bool _updating;

        public ChoiceRow(SettingsView owner, ChoiceSetting definition)
            : base(owner, definition)
        {
            _choice = definition;
            _combo = new ComboBox { ItemsSource = definition.Options, DisplayMemberPath = nameof(ChoiceOption.Title) };
            SetAutomationName(_combo);
            Load();
            _combo.SelectionChanged += (_, _) =>
            {
                if (_updating || _combo.SelectedItem is not ChoiceOption option)
                {
                    return;
                }

                if (!Write(() => Settings.Set(_choice.Key, option.Value)))
                {
                    Load();
                }
            };

            if (Compact)
            {
                Root = Stacked(_combo);
            }
            else
            {
                _combo.Width = 220;
                Root = SideBySide(_combo);
            }
        }

        public override void Refresh() => Load();

        private void Load()
        {
            _updating = true;
            try
            {
                var value = Settings.GetString(_choice.Key);
                _combo.SelectedItem = _choice.Options.FirstOrDefault(o => o.Value == value);
            }
            finally
            {
                _updating = false;
            }
        }
    }

    private sealed class TextRow : SettingRow
    {
        private readonly TextBox _box;

        public TextRow(SettingsView owner, TextSetting definition)
            : base(owner, definition)
        {
            _box = new TextBox { Text = Settings.GetString(definition.Key) };
            SetAutomationName(_box);
            _box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                    e.Handled = true;
                }
            };
            _box.LostFocus += (_, _) => Commit();

            if (Compact)
            {
                Root = Stacked(_box);
            }
            else
            {
                _box.Width = 240;
                Root = SideBySide(_box);
            }
        }

        public override void Refresh()
        {
            if (!_box.IsKeyboardFocusWithin)
            {
                _box.Text = Settings.GetString(Definition.Key);
            }
        }

        public override void Flush() => Commit();

        private void Commit()
        {
            var text = _box.Text;
            if (text == Settings.GetString(Definition.Key))
            {
                return;
            }

            if (!Write(() => Settings.Set(Definition.Key, text)))
            {
                _box.Text = Settings.GetString(Definition.Key);
            }
        }
    }

    private sealed class ButtonRow : SettingRow
    {
        public ButtonRow(SettingsView owner, ButtonSetting definition)
            : base(owner, definition)
        {
            var button = new Button { Content = definition.Title, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 5, 14, 5) };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await Owner._actions.ExecuteAsync($"{Owner._moduleId}/{definition.ActionId}", ActionTrigger.Ui);
                    UiTheme.ShowError(ErrorText, null);
                }
                catch (Exception ex)
                {
                    UiTheme.ShowError(ErrorText, string.Format(UiStrings.ActionFailedFormat, UiTheme.FormatError(ex)));
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };

            var panel = new StackPanel();
            panel.Children.Add(button);
            if (!Compact && !string.IsNullOrWhiteSpace(definition.Description))
            {
                var description = UiTheme.Label(definition.Description, 12, secondary: true);
                description.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(description);
            }

            Root = Finish(panel);
        }

        public override void Refresh()
        {
        }
    }

    /// <summary>Невідомий різновид опису (з нових версій SDK): показуємо лише назву, щоб нічого не ламалося.</summary>
    private sealed class UnsupportedRow : SettingRow
    {
        public UnsupportedRow(SettingsView owner, SettingDefinition definition)
            : base(owner, definition)
        {
            Root = Finish(TitleLabel());
        }

        public override void Refresh()
        {
        }
    }
}
