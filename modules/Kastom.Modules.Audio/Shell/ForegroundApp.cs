using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kastom.Modules.Audio.Shell;

/// <summary>Процес вікна, що зараз на передньому плані. Ім'я — без ".exe", у нижньому регістрі.</summary>
internal readonly record struct ForegroundProcess(int ProcessId, string ProcessName);

/// <summary>Вікно зі списку за порядком показу (z-order) з тим, що потрібно, щоб вирішити, чи воно «програма перед очима».</summary>
internal readonly record struct WindowInfo(IntPtr Handle, int ProcessId, bool IsVisible, bool IsMinimized, bool IsToolWindow, bool IsCloaked);

/// <summary>Звідки дії «активна програма» дізнаються, яка програма зараз перед очима.</summary>
internal interface IForegroundApp
{
    /// <summary>Null — вікна немає або процес не вдалося визначити.</summary>
    ForegroundProcess? Get();
}

internal sealed class ForegroundApp : IForegroundApp
{
    private const string FrameHostName = "applicationframehost";
    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExTransparent = 0x20;
    private const long WsExNoActivate = 0x08000000;
    private const int DwmwaCloaked = 14;

    // Запобіжник від зациклення, якщо порядок вікон змінюється, поки ми його обходимо.
    private const int MaxWindowsWalked = 2000;

    private delegate bool EnumChildProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    /// <summary>
    /// Перше вікно іншого процесу серед <paramref name="below"/> (вікон під передніми, від верхніх до нижніх): видиме,
    /// не згорнуте, не допоміжне (панель інструментів, підказка) і не приховане віртуальним робочим столом.
    /// </summary>
    internal static WindowInfo? PickBehindOwn(IEnumerable<WindowInfo> below, int ownProcessId) =>
        below.Where(w => w.ProcessId != ownProcessId && w.IsVisible && !w.IsMinimized && !w.IsToolWindow && !w.IsCloaked)
            .Select(w => (WindowInfo?)w)
            .FirstOrDefault();

    public ForegroundProcess? Get()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;

            // Віджет чи кнопка лаунчера забирають передній план собі: «активна» тоді програма, що лежить під ним.
            if (ProcessOf(window) == Environment.ProcessId)
            {
                if (PickBehindOwn(WindowsBelow(window), Environment.ProcessId) is not { } behind) return null;
                window = behind.Handle;
            }

            return Describe(window);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ForegroundProcess? Describe(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        var name = NameOf((int)processId);
        if (name is null) return null;

        // Програми Магазину живуть усередині ApplicationFrameHost; справжній процес — дочірнє вікно.
        if (name == FrameHostName)
        {
            var real = 0u;
            EnumChildWindows(window, (child, _) =>
            {
                GetWindowThreadProcessId(child, out var childProcess);
                if (childProcess == 0 || childProcess == processId) return true;
                real = childProcess;
                return false;
            }, IntPtr.Zero);

            if (real != 0 && NameOf((int)real) is { } realName) return new ForegroundProcess((int)real, realName);
        }

        return new ForegroundProcess((int)processId, name);
    }

    private static IEnumerable<WindowInfo> WindowsBelow(IntPtr start)
    {
        var window = start;
        for (var i = 0; i < MaxWindowsWalked; i++)
        {
            window = GetWindow(window, GwHwndNext);
            if (window == IntPtr.Zero) yield break;

            var exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
            var cloaked = DwmGetWindowAttribute(window, DwmwaCloaked, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
            yield return new WindowInfo(
                window,
                ProcessOf(window),
                IsWindowVisible(window),
                IsIconic(window),
                // Прозорі накладки (оверлеї відеокарти, лічильники FPS) і вікна, що не беруть фокус, — теж не «програма перед очима».
                (exStyle & (WsExToolWindow | WsExTransparent | WsExNoActivate)) != 0,
                cloaked);
        }
    }

    private static int ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    private static string? NameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
