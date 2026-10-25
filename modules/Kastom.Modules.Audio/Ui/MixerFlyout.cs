using System.Windows;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Панель мікшера біля області сповіщень (З11). ЗАГЛУШКА елемента E0: справжню реалізацію пише елемент E2.</summary>
public sealed class MixerFlyout : Window
{
    public MixerFlyout(MixerHost host)
    {
        Host = host;
    }

    public MixerHost Host { get; }

    /// <summary>
    /// Показати біля області сповіщень того монітора, де лежить точка (фізичні пікселі екрана, як Cursor.Position),
    /// і зробити активною. Якщо панель уже відкрита — лише перемістити й активувати.
    /// </summary>
    public void ShowNear(Point screenPixels)
    {
    }

    /// <summary>
    /// Клік по значку в треї: відкрита — сховати, закрита — показати. Клік, що саме закрив панель
    /// (вона ховається, коли втрачає фокус), не відкриває її знову.
    /// </summary>
    public void Toggle(Point screenPixels)
    {
    }
}
