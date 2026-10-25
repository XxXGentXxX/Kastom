using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Kastom.Launcher.Services;
using Kastom.Launcher.Settings;

namespace Kastom.Launcher.Notifications;

/// <summary>Мала напівпрозора підказка внизу екрана: не бере фокус, пропускає кліки, зникає сама.</summary>
internal sealed class OsdWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const double BottomMargin = 56;

    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1.6);
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(250));

    private readonly ScreenPolicy _screens;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer;
    private int _generation;

    public OsdWindow(ScreenPolicy screens)
    {
        _screens = screens;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        MaxWidth = Math.Max(240, screens.WorkArea.Width * 0.6);

        _text = UiTheme.Label("", 16);
        _text.TextAlignment = TextAlignment.Center;
        _text.Margin = new Thickness(24, 12, 24, 12);
        Content = UiTheme.WindowSurface(_text, 12);

        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = Lifetime };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            StartFade();
        };

        SourceInitialized += (_, _) => MakeClickThrough();
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowText(string text)
    {
        _generation++;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _text.Text = text;

        if (!IsVisible)
        {
            Reposition();
            Show();
        }

        Reposition();
        _timer.Stop();
        _timer.Start();
    }

    private void StartFade()
    {
        var generation = _generation;
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            // Новий текст прийшов під час згасання — вікно лишається.
            if (generation == _generation)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Reposition()
    {
        var work = _screens.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 48;
        if (double.IsNaN(width))
        {
            width = 240;
        }

        Left = work.Left + (work.Width - width) / 2;
        Top = work.Bottom - height - BottomMargin;
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExTransparent | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
