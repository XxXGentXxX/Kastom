using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Kastom.Launcher.Resources;
using Kastom.Launcher.Settings;

namespace Kastom.Launcher.Widgets;

/// <summary>Вікно одного віджета: без рамки, з вузькою шапкою (назва + «×»), перетягується за шапку, змінює розмір за краї.</summary>
public sealed class WidgetWindow : Window
{
    private const double HeaderHeight = 30;

    private readonly Border _body;
    private bool _closingRaised;

    public WidgetWindow(string title, FrameworkElement content)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false;
        // Віджет на столі не забирає фокус, коли з'являється (зокрема при автозапуску).
        ShowActivated = false;
        MinWidth = 160;
        MinHeight = 80;
        UseLayoutRounding = true;

        // Краї для зміни розміру без системної шапки; перетягування шапки робимо самі (DragMove).
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var titleText = UiTheme.Label(title, 12, secondary: true);
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleText.TextWrapping = TextWrapping.NoWrap;
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(12, 0, 0, 0);

        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = HeaderHeight - 4,
            Margin = new Thickness(0, 2, 4, 2),
            Padding = new Thickness(0),
            FontSize = 16,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = UiStrings.WidgetClose,
        };
        System.Windows.Automation.AutomationProperties.SetName(close, UiStrings.WidgetClose);
        close.Click += (_, _) => Close();

        var header = new Grid { Height = HeaderHeight, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titleText);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Кнопку вже відпущено.
            }
        };

        var body = new Border { Padding = new Thickness(8, 0, 8, 8), Child = content };
        Grid.SetRow(body, 1);
        _body = body;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        layout.Children.Add(body);

        Content = UiTheme.WindowSurface(layout);

        Closing += (_, _) => _closingRaised = true;

        // Вміст створив модуль: після закриття відпускаємо його, щоб віджет не тримав вікно живим.
        Closed += (_, _) =>
        {
            LostOwner = !_closingRaised;
            _body.Child = null;
            Content = null;
        };

        LocationChanged += (_, _) => RaisePlacementChanged();
        SizeChanged += (_, _) => RaisePlacementChanged();
    }

    /// <summary>
    /// Вікно зникло без закриття: Windows знищила його разом із власником (Провідник перезапустився).
    /// Користувач віджет не закривав, тож він лишається видимим у стані.
    /// </summary>
    public bool LostOwner { get; private set; }

    /// <summary>Хост вмикає після Show(), щоб початкове розташування не вважалося зміною користувача.</summary>
    public bool TrackChanges { get; set; }

    /// <summary>Вікно перемістили чи змінили його розмір.</summary>
    public event EventHandler? PlacementChanged;

    private void RaisePlacementChanged()
    {
        if (TrackChanges)
        {
            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
