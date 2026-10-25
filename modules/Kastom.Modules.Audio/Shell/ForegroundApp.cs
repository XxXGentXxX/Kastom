using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kastom.Modules.Audio.Shell;

/// <summary>Процес вікна, що зараз на передньому плані. Ім'я — без ".exe", у нижньому регістрі.</summary>
internal readonly record struct ForegroundProcess(int ProcessId, string ProcessName);

/// <summary>Звідки дії «активна програма» дізнаються, яка програма зараз перед очима.</summary>
internal interface IForegroundApp
{
    /// <summary>Null — вікна немає або процес не вдалося визначити.</summary>
    ForegroundProcess? Get();
}

internal sealed class ForegroundApp : IForegroundApp
{
    private const string FrameHostName = "applicationframehost";

    private delegate bool EnumChildProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);

    public ForegroundProcess? Get()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;

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
        catch (Exception)
        {
            return null;
        }
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
