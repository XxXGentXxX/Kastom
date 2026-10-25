using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kastom.Launcher.Widgets;

/// <summary>
/// Шар робочого столу для віджетів (В0, 🟡, вимикач — LauncherState.WidgetsOnDesktop, типово вимкнений). Віджет
/// отримує власника — вікно робочого столу Progman. Win+D ховає звичайні вікна, а підлеглі вікна завжди лежать над
/// своїм власником, тож віджет лишається на столі. Назва класу Progman недокументована, але не міняється з Windows 95.
/// Такий віджет лежить під вікнами програм, як значки робочого столу, і не бере фокус (WS_EX_NOACTIVATE): Win+D
/// активує Progman, і без цього Windows передала б фокус його підлеглому вікну — віджету, а друге Win+D уже не
/// повернуло б згорнуті вікна (перевірено наживо 2026-10-25). Тому поля введення у віджетах на столі не працюють.
/// Ціна: власник з іншого процесу зв'язує черги вводу нашого потоку інтерфейсу й потоку робочого столу.
/// Коли Провідник перезапускається, старий Progman зникає; Windows розсилає всім вікнам верхнього рівня
/// TaskbarCreated, і хост перевідкриває віджети з новим власником.
/// </summary>
internal sealed class DesktopLayer : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    private readonly HwndSource _watcher;
    private readonly int _taskbarCreated;

    public DesktopLayer()
    {
        _taskbarCreated = (int)RegisterWindowMessage("TaskbarCreated");

        // Невидиме вікно верхнього рівня: розсилки не доходять до вікон лише для повідомлень (HWND_MESSAGE).
        _watcher = new HwndSource(new HwndSourceParameters("KastomDesktopWatch")
        {
            Width = 0,
            Height = 0,
            WindowStyle = WsPopup,
        });
        _watcher.AddHook(WndProc);
    }

    /// <summary>Провідник перезапустився: віджети, що жили на шарі робочого столу, треба відкрити знову.</summary>
    public event EventHandler? ShellRestarted;

    /// <summary>Чи є вже вікно робочого столу (після перезапуску Провідника воно з'являється не одразу).</summary>
    public static bool DesktopExists() => FindWindow("Progman", null) != IntPtr.Zero;

    /// <summary>Зробити вікно робочого столу власником <paramref name="window"/>. Викликати до Show().</summary>
    public static bool TryAttach(Window window)
    {
        var desktop = FindWindow("Progman", null);
        if (desktop == IntPtr.Zero) return false;
        new WindowInteropHelper(window).Owner = desktop;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate);
        };
        return true;
    }

    public void Dispose()
    {
        _watcher.RemoveHook(WndProc);
        _watcher.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && msg == _taskbarCreated) ShellRestarted?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
