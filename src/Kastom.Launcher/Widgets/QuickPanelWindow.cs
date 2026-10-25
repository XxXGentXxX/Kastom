using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kastom.Launcher.Settings;

namespace Kastom.Launcher.Widgets;

/// <summary>Швидка панель біля значка в області сповіщень; закривається, коли втрачає фокус.</summary>
public sealed class QuickPanelWindow : Window
{
    private const double PanelWidth = 360;
    private const double ScreenMargin = 12;

    private bool _closing;

    public QuickPanelWindow(FrameworkElement content)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        UseLayoutRounding = true;
        Width = PanelWidth;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * 0.7;
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
        Activate();
    }

    private void Reposition()
    {
        var work = SystemParameters.WorkArea;

        // Робоча область вже виключає панель задач; порівнюємо її з екраном, щоб знайти, з якого боку вона.
        var taskbarLeft = work.Left > 0;
        var taskbarTop = work.Top > 0;
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
