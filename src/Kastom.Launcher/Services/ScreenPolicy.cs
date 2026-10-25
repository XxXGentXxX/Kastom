using System.Windows;

namespace Kastom.Launcher.Services;

/// <summary>
/// Де відкривати вікна лаунчера й чи можна брати фокус. Звичайно — робоча область головного монітора
/// (там значок у треї) і фокус, як у будь-якої програми; з --test-screen — задана область і жодного фокусу.
/// </summary>
public sealed class ScreenPolicy(Rect? testScreen)
{
    /// <summary>Режим живої перевірки (--test-screen).</summary>
    public bool IsTestScreen => testScreen is not null;

    /// <summary>Чи можна вікнам лаунчера забирати фокус у програми користувача.</summary>
    public bool MayActivate => testScreen is null;

    /// <summary>Робоча область для швидкої панелі, підказок, нових віджетів і першого відкриття головного вікна.</summary>
    public Rect WorkArea => testScreen ?? SystemParameters.WorkArea;
}
