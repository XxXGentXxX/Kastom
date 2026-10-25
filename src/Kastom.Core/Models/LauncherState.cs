namespace Kastom.Core.Models;

/// <summary>Усе, що лаунчер пам'ятає між запусками (launcher.json).</summary>
public sealed class LauncherState
{
    /// <summary>Встановлені модулі за ідентифікатором.</summary>
    public Dictionary<string, InstalledModuleState> Modules { get; set; } = new();

    /// <summary>
    /// Модулі, видалені, поки їхні збірки були завантажені: Windows тримає такі файли заблокованими,
    /// тож папки стираються під час наступного запуску, ще до завантаження модулів.
    /// </summary>
    public List<string> PendingRemovals { get; set; } = new();

    /// <summary>
    /// Прив'язки клавіш: повний ідентифікатор дії ("audio/mute-mic") → поєднання ("Ctrl+Alt+M").
    /// Порожній рядок означає, що користувач навмисно прибрав поєднання. Немає запису — діє типове.
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    /// <summary>Загальний вимикач усіх гарячих клавіш.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    public UpdateMode UpdateMode { get; set; } = UpdateMode.Notify;

    public bool Autostart { get; set; } = true;

    /// <summary>Інша адреса каталогу (для тестерів). Null — типова.</summary>
    public string? CatalogUrlOverride { get; set; }

    /// <summary>Розташування й видимість віджетів: "kastom/modules", "demo/counter".</summary>
    public Dictionary<string, WidgetPlacement> Widgets { get; set; } = new();
}

public sealed class InstalledModuleState
{
    public required string Version { get; set; }

    /// <summary>Попередня встановлена версія, на яку можна відкотитися.</summary>
    public string? PreviousVersion { get; set; }

    public bool Enabled { get; set; } = true;
}

public enum UpdateMode
{
    /// <summary>Повідомити про оновлення, ставити після підтвердження.</summary>
    Notify,

    /// <summary>Ставити оновлення модулів самостійно.</summary>
    Automatic,
}

public sealed class WidgetPlacement
{
    public bool Visible { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}
