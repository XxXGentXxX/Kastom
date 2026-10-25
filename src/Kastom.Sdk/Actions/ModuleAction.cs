using Kastom.Sdk.Hotkeys;

namespace Kastom.Sdk.Actions;

/// <summary>
/// Дія модуля: «вимкнути мікрофон», «наступний пристрій виводу». Її можна прив'язати до гарячих клавіш,
/// поставити в сектор кругового меню або викликати кнопкою віджета.
/// Повний ідентифікатор дії в лаунчері — "&lt;moduleId&gt;/&lt;Id&gt;", наприклад "audio/mute-mic".
/// </summary>
public sealed class ModuleAction
{
    /// <summary>Ідентифікатор у межах модуля: латиниця в нижньому регістрі, цифри, дефіс.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Поєднання клавіш за замовчуванням. Лаунчер не призначить його, якщо воно вже зайняте.</summary>
    public HotkeyGesture? DefaultHotkey { get; init; }

    /// <summary>Виконання. Викликається в потоці інтерфейсу.</summary>
    public required Func<ActionContext, Task> Execute { get; init; }
}

public enum ActionTrigger
{
    Ui,
    Hotkey,
    RadialMenu,
    Widget,
}

public sealed class ActionContext
{
    public required ActionTrigger Trigger { get; init; }
}

public interface IActionRegistry
{
    void Register(ModuleAction action);

    IReadOnlyList<ModuleAction> Actions { get; }
}
