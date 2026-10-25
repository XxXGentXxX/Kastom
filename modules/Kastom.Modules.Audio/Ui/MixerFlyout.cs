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

    private readonly MixerView _view;
    private long _hiddenAt = -ReopenGuardMilliseconds * 10;

    // Екран, біля якого панель показано востаннє: потрібен, щоб утримати її нижній край біля панелі задач, коли висота змінюється.
    private ScreenPlacement? _screen;

    public MixerFlyout(MixerHost host)
    {
        Host = host;
        _view = new MixerView(host, MixerMode.Flyout);

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
            Content = _view,
        };

        // Плитки внизу, поза прокруткою: довгий список пристроїв і програм не ховає їх.
        var body = new DockPanel { LastChildFill = true };
        var tiles = _view.Tiles!;
        tiles.Margin = new Thickness(12, 4, 12, 12);
        DockPanel.SetDock(tiles, Dock.Bottom);
        body.Children.Add(tiles);
        body.Children.Add(scroll);

        var frame = new Border { BorderThickness = new Thickness(1), Child = body };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardStroke);
        Content = frame;

        SourceInitialized += (_, _) => RoundCorners();
        SizeChanged += OnSizeChanged;
        Deactivated += (_, _) => HideRemembering();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            HideRemembering();
            e.Handled = true;
        };
    }

    public MixerHost Host { get; }

    /// <summary>Мікшер у панелі; для тестів.</summary>
    internal MixerView View => _view;

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
    /// Вікно панелі у фізичних пікселях: розмір за висотою вмісту <paramref name="heightDip"/> (незалежні від DPI
    /// одиниці WPF) і <paramref name="scale"/> монітора, позиція — <see cref="ComputePlacement"/>.
    /// </summary>
    internal static Rect ComputeFrame(Rect workAreaPx, Rect boundsPx, double scale, double heightDip)
    {
        var size = new Size(Math.Round(PanelWidth * scale), Math.Ceiling(heightDip * scale));
        var origin = ComputePlacement(workAreaPx, boundsPx, size);
        return new Rect(origin, size);
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
        _screen = new ScreenPlacement(work, bounds, scale);

        // Схований мікшер знімків не малює: без цього вміст міряється порожнім і панель виходить не того розміру.
        _view.RefreshNow();

        // Уже показувана панель має відкладені зміни розмітки: без цього Measure повернув би розмір з минулого разу.
        UpdateLayout();

        // Розмір рахуємо заздалегідь: позицію в пікселях треба знати до показу.
        MaxHeight = work.Height * MaxHeightShare / scale;
        var content = (UIElement)Content;
        content.Measure(new Size(PanelWidth, MaxHeight));
        var frame = ComputeFrame(work, bounds, scale, Math.Min(content.DesiredSize.Height, MaxHeight));

        Left = frame.X / scale;
        Top = frame.Y / scale;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        Place(handle, frame);

        if (!IsVisible) Show();
        Activate();

        // Показ міг перерахувати розмір під DPI вікна; ще раз ставимо точні пікселі.
        Place(handle, frame);
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

    // Вікно росте само (SizeToContent): коли додається рядок, нижній край має лишитися біля панелі задач, а не поїхати вниз.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || _screen is not { } screen || !e.HeightChanged) return;

        var frame = ComputeFrame(screen.WorkArea, screen.Bounds, screen.Scale, e.NewSize.Height);
        Place(new WindowInteropHelper(this).Handle, frame);
    }

    private static void Place(IntPtr handle, Rect frame) =>
        SetWindowPos(handle, HwndTopmost, (int)Math.Round(frame.X), (int)Math.Round(frame.Y), (int)frame.Width, (int)frame.Height, SwpNoActivate);

    private readonly record struct ScreenPlacement(Rect WorkArea, Rect Bounds, double Scale);

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
