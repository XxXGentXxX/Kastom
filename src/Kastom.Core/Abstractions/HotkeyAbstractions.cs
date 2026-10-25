using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Abstractions;

/// <summary>Реєстрація поєднань у Windows (RegisterHotKey). Реалізація — у проєкті лаунчера.</summary>
public interface IHotkeyBackend : IDisposable
{
    /// <summary>False — поєднання вже зайняте Windows чи іншою програмою.</summary>
    bool TryRegister(int id, HotkeyGesture gesture);

    void Unregister(int id);

    /// <summary>Натиснуто зареєстроване поєднання; аргумент — id з <see cref="TryRegister"/>.</summary>
    event EventHandler<int>? Pressed;
}

public enum HotkeyBindingStatus
{
    /// <summary>Поєднання не призначене.</summary>
    Unassigned,

    /// <summary>Зареєстроване й працює.</summary>
    Active,

    /// <summary>Те саме поєднання в іншої нашої дії.</summary>
    ConflictInternal,

    /// <summary>Windows або інша програма вже тримає це поєднання.</summary>
    TakenBySystem,

    /// <summary>Усі клавіші вимкнено загальним вимикачем.</summary>
    Disabled,
}

public sealed record HotkeyBindingInfo(RegisteredAction Action, HotkeyGesture? Gesture, HotkeyBindingStatus Status);

public sealed record HotkeyAssignResult(bool Success, string? Error = null, string? ConflictingActionId = null);

/// <summary>Прив'язки клавіш до дій лаунчера й модулів.</summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>По одному запису на кожну дію з <see cref="IActionCatalog"/>.</summary>
    IReadOnlyList<HotkeyBindingInfo> Bindings { get; }

    /// <summary>Загальний вимикач (зберігається в стані лаунчера).</summary>
    bool Enabled { get; set; }

    /// <summary>Призначити поєднання; null прибирає його.</summary>
    HotkeyAssignResult Assign(string globalActionId, HotkeyGesture? gesture);

    /// <summary>Повернути поєднання за замовчуванням з <see cref="Kastom.Sdk.Actions.ModuleAction.DefaultHotkey"/>.</summary>
    HotkeyAssignResult ResetToDefault(string globalActionId);

    /// <summary>
    /// Тимчасово зняти всі поєднання з реєстрації (поки користувач натискає нове поєднання на сторінці клавіш:
    /// інакше Windows перехопить уже зареєстроване поєднання раніше, ніж його побачить вікно).
    /// Dispose повертає реєстрацію. Вкладені виклики допустимі.
    /// </summary>
    IDisposable Suspend();

    event EventHandler? Changed;
}
