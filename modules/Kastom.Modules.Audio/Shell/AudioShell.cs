using System.Diagnostics;
using Kastom.Modules.Audio.Model;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.Shell;

/// <summary>Переходи на сторінки параметрів Windows і повідомлення про помилки.</summary>
internal sealed class AudioShell(IModuleLogger log, INotificationService osd, Action<string>? launch = null) : IAudioShell
{
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

    public void ReportError(string userMessage, Exception? exception)
    {
        log.Error(userMessage, exception);
        osd.ShowOsd(userMessage);
    }

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
