using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Панель мікшера біля області сповіщень (З11). Не закривається, а ховається: так вона відкривається миттєво,
/// а мікшер у ній, поки схований, не малює й не міряє рівні.
/// </summary>
public sealed class MixerFlyout : Window
{
    private const double PanelWidth = 380;
    private const double EdgeMargin = 12;
    private const double MaxHeightShare = 0.8;

    // Клік по значку, що забрав фокус у панелі, не має відкривати її знову.
    private const long ReopenGuardMilliseconds = 400;

    private long _hiddenAt = -ReopenGuardMilliseconds * 10;

    public MixerFlyout(MixerHost host)
    {
        Host = host;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        Title = Strings.ModuleTitle;
        SetResourceReference(BackgroundProperty, ThemeKeys.SolidBackground);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new MixerView(host, MixerMode.Flyout),
        };
        var frame = new Border { BorderThickness = new Thickness(1), Child = scroll };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardStroke);
        Content = frame;

        SourceInitialized += (_, _) => RoundCorners();
        Deactivated += (_, _) => HideRemembering();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            HideRemembering();
            e.Handled = true;
        };
    }

    public MixerHost Host { get; }

    /// <summary>
    /// Куди поставити панель розміром <paramref name="sizePx"/> на моніторі з робочою областю
    /// <paramref name="workAreaPx"/> і межами <paramref name="boundsPx"/> (усе у фізичних пікселях): у кут робочої області
    /// біля панелі задач, з відступом 12 px. Край панелі задач видно з того, з якого боку робоча область менша за монітор.
    /// </summary>
    internal static Point ComputePlacement(Rect workAreaPx, Rect boundsPx, Size sizePx)
    {
        var right = workAreaPx.Right - sizePx.Width - EdgeMargin;
        var left = workAreaPx.Left + EdgeMargin;
        var bottom = workAreaPx.Bottom - sizePx.Height - EdgeMargin;
        var top = workAreaPx.Top + EdgeMargin;

        Point corner;
        if (workAreaPx.Top > boundsPx.Top) corner = new Point(right, top);
        else if (workAreaPx.Left > boundsPx.Left) corner = new Point(left, bottom);
        else corner = new Point(right, bottom);

        // Висока панель не має виходити за робочу область.
        return new Point(
            Math.Max(workAreaPx.Left, corner.X),
            Math.Max(workAreaPx.Top, corner.Y));
    }

    /// <summary>
    /// Показати біля області сповіщень того монітора, де лежить точка (фізичні пікселі екрана, як Cursor.Position),
    /// і зробити активною. Якщо панель уже відкрита — лише перемістити й активувати.
    /// </summary>
    public void ShowNear(Point screenPixels)
    {
        var point = new System.Drawing.Point((int)Math.Round(screenPixels.X), (int)Math.Round(screenPixels.Y));
        var screen = System.Windows.Forms.Screen.FromPoint(point);
        var work = ToRect(screen.WorkingArea);
        var bounds = ToRect(screen.Bounds);
        var scale = MonitorScale(point);

        // Розмір рахуємо заздалегідь: позицію в пікселях треба знати до показу.
        MaxHeight = work.Height * MaxHeightShare / scale;
        var content = (UIElement)Content;
        content.Measure(new Size(PanelWidth, MaxHeight));
        var size = new Size(Math.Round(PanelWidth * scale), Math.Ceiling(Math.Min(content.DesiredSize.Height, MaxHeight) * scale));
        var origin = ComputePlacement(work, bounds, size);

        Left = origin.X / scale;
        Top = origin.Y / scale;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        Place(handle, origin, size);

        if (!IsVisible) Show();
        Activate();

        // Показ міг перерахувати розмір під DPI вікна; ще раз ставимо точні пікселі.
        Place(handle, origin, size);
    }

    /// <summary>
    /// Клік по значку в треї: відкрита — сховати, закрита — показати. Клік, що саме закрив панель
    /// (вона ховається, коли втрачає фокус), не відкриває її знову.
    /// </summary>
    public void Toggle(Point screenPixels)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (Environment.TickCount64 - _hiddenAt < ReopenGuardMilliseconds) return;
        ShowNear(screenPixels);
    }

    private void HideRemembering()
    {
        if (!IsVisible) return;
        _hiddenAt = Environment.TickCount64;
        Hide();
    }

    private static Rect ToRect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    private static void Place(IntPtr handle, Point origin, Size size) =>
        SetWindowPos(handle, HwndTopmost, (int)Math.Round(origin.X), (int)Math.Round(origin.Y), (int)size.Width, (int)size.Height, SwpNoActivate);

    private static double MonitorScale(System.Drawing.Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MonitorDpiEffective, out var dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch
        {
            // Старі Windows без shcore: беремо 100 %.
        }

        return 1.0;
    }

    private void RoundCorners()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var round = DwmCornerRound;
            DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, sizeof(int));
        }
        catch
        {
            // Без заокруглення панель лишається прямокутною: це лише вигляд.
        }
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorDpiEffective = 0;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
