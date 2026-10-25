using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kastom.Launcher.Services;
using Kastom.Launcher.Settings;

namespace Kastom.Launcher.Widgets;

/// <summary>Швидка панель біля значка в області сповіщень; закривається, коли втрачає фокус.</summary>
public sealed class QuickPanelWindow : Window
{
    private const double PanelWidth = 360;
    private const double ScreenMargin = 12;

    private readonly ScreenPolicy _screens;
    private bool _closing;

    public QuickPanelWindow(FrameworkElement content, ScreenPolicy screens)
    {
        _screens = screens;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        // Без фокусу (режим --test-screen) панель не закриється сама від кліку деінде, і Esc до неї не дійде —
        // лише повторний виклик (значок у треї, гаряча клавіша).
        ShowActivated = screens.MayActivate;
        UseLayoutRounding = true;
        Width = PanelWidth;
        SizeToContent = SizeToContent.Height;
        MaxHeight = screens.WorkArea.Height * 0.7;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12),
            Content = content,
        };
        Content = UiTheme.WindowSurface(scroll, 10);

        Deactivated += (_, _) => CloseOnce();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseOnce();
            }
        };
        SizeChanged += (_, _) => Reposition();
    }

    /// <summary>Показати в куті робочої області, найближчому до панелі задач (значок трея лежить там).</summary>
    public void ShowNearTray()
    {
        Reposition();
        Show();
        Reposition();
        if (_screens.MayActivate) Activate();
    }

    private void Reposition()
    {
        var work = _screens.WorkArea;

        // Робоча область головного монітора вже виключає панель задач: зсув від нуля показує, з якого боку вона.
        // Тестова область (--test-screen) — не головний монітор: там панель просто в правому нижньому куті.
        var taskbarLeft = !_screens.IsTestScreen && work.Left > 0;
        var taskbarTop = !_screens.IsTestScreen && work.Top > 0;
        var width = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? PanelWidth : ActualWidth;
        var height = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? MaxHeight : ActualHeight;

        Left = taskbarLeft ? work.Left + ScreenMargin : work.Right - width - ScreenMargin;
        Top = taskbarTop ? work.Top + ScreenMargin : work.Bottom - height - ScreenMargin;
    }

    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }
}
