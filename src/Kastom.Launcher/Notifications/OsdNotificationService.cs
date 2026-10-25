using System.Windows;
using Kastom.Launcher.Services;
using Kastom.Sdk.Services;

namespace Kastom.Launcher.Notifications;

/// <summary>Підказки на екрані (OSD) для модулів і лаунчера.</summary>
public sealed class OsdNotificationService(ScreenPolicy screens) : INotificationService
{
    private OsdWindow? _window;

    public void ShowOsd(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Show(text);
        }
        else
        {
            dispatcher.BeginInvoke(() => Show(text));
        }
    }

    // Одне вікно на всі підказки: створюється вперше при першому виклику, далі лише міняється текст.
    private void Show(string text)
    {
        _window ??= new OsdWindow(screens);
        _window.ShowText(text);
    }
}
