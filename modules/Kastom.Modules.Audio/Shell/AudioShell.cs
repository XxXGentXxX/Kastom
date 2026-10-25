using System.Diagnostics;
using System.Runtime.InteropServices;
using Kastom.Modules.Audio.Model;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.Shell;

/// <summary>Переходи на сторінки параметрів Windows, відкриття програм і повідомлення про помилки.</summary>
/// <param name="launch">Відкрити адресу чи exe через оболонку; у тестах підставляється.</param>
/// <param name="showWindow">Вивести наперед головне вікно одного з процесів; true — вікно було. У тестах підставляється.</param>
/// <param name="startProcess">Запустити програму з аргументами (Провідник); у тестах підставляється.</param>
internal sealed class AudioShell(
    IModuleLogger log,
    INotificationService osd,
    Action<string>? launch = null,
    Func<IReadOnlyList<int>, bool>? showWindow = null,
    Action<string, string>? startProcess = null) : IAudioShell
{
    private const int SwRestore = 9;

    public const string SoundUri = "ms-settings:sound";
    public const string InputPropertiesUri = "ms-settings:sound-defaultinputproperties";
    public const string OutputPropertiesUri = "ms-settings:sound-defaultoutputproperties";
    public const string AppVolumeUri = "ms-settings:apps-volume";
    public const string TaskbarUri = "ms-settings:taskbar";

    public void OpenSoundSettings() => Open(SoundUri);

    public void OpenInputProperties() => Open(InputPropertiesUri);

    public void OpenOutputProperties() => Open(OutputPropertiesUri);

    public void OpenAppVolumeSettings() => Open(AppVolumeUri);

    public void OpenTaskbarSettings() => Open(TaskbarUri);

    public void OpenApp(AudioApp app)
    {
        if (app.ExePath is not { } path) return;

        bool shown;
        try
        {
            shown = (showWindow ?? ShowMainWindow)(app.ProcessIds);
        }
        catch (Exception ex)
        {
            log.Warn($"Не вдалося вивести вікно {path}: {ex.Message}");
            shown = false;
        }

        // Вікна немає (програма в треї): той самий exe ще раз — програми з одним екземпляром покажуть своє вікно.
        if (!shown) Open(path);
    }

    public void RevealApp(AudioApp app)
    {
        if (app.ExePath is not { } path) return;

        try
        {
            if (startProcess is not null) startProcess("explorer.exe", RevealArguments(path));
            else Process.Start(new ProcessStartInfo("explorer.exe", RevealArguments(path)) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            log.Warn($"Не вдалося показати {path} у Провіднику: {ex.Message}");
        }
    }

    /// <summary>Аргументи Провідника, що виділяють файл у його теці.</summary>
    internal static string RevealArguments(string path) => $"/select,\"{path}\"";

    public void ReportError(string userMessage, Exception? exception)
    {
        log.Error(userMessage, exception);
        osd.ShowOsd(userMessage);
    }

    // Головне вікно першого з процесів, що його має. Згорнуте відновлюється; сховане (програма в треї) вікна не має.
    private static bool ShowMainWindow(IReadOnlyList<int> processIds)
    {
        foreach (var id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;

                if (IsIconic(window)) ShowWindow(window, SwRestore);
                SetForegroundWindow(window);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Процес уже завершився: беремо наступний.
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void Open(string uri)
    {
        try
        {
            if (launch is not null) launch(uri);
            else Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            log.Warn($"Не вдалося відкрити {uri}: {ex.Message}");
        }
    }
}
