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

/// <summary>
/// Прив'язка дії: <see cref="Gesture"/> — ефективне поєднання (адреса розділу + клавіша дії), null — клавіші немає;
/// <see cref="DefaultGesture"/> — те, що дала б типова клавіша дії за теперішньої адреси.
/// </summary>
public sealed record HotkeyBindingInfo(
    RegisteredAction Action,
    HotkeyGesture? Gesture,
    HotkeyBindingStatus Status,
    HotkeyGesture? DefaultGesture = null);

/// <summary>Розділ клавіш (Г2): лаунчер або модуль, у якого зараз є дії. Адреса — модифікатори, спільні для дій розділу.</summary>
public sealed record HotkeySectionInfo(string Id, string Name, HotkeyModifiers Address, HotkeyModifiers DefaultAddress)
{
    public bool IsDefaultAddress => Address == DefaultAddress;
}

public sealed record HotkeyAssignResult(bool Success, string? Error = null, string? ConflictingActionId = null);

/// <summary>Прив'язки клавіш до дій лаунчера й модулів за схемою «адреса розділу + клавіша дії».</summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>По одному запису на кожну дію з <see cref="IActionCatalog"/>.</summary>
    IReadOnlyList<HotkeyBindingInfo> Bindings { get; }

    /// <summary>Розділи з діями: лаунчер першим, далі модулі за назвою.</summary>
    IReadOnlyList<HotkeySectionInfo> Sections { get; }

    /// <summary>Загальний вимикач (зберігається в стані лаунчера).</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Змінити адресу розділу: усі його дії реєструються наново. Конфлікти нових поєднань не відхиляють зміну —
    /// вони видні в <see cref="Bindings"/>, а решта дій працює.
    /// </summary>
    HotkeyAssignResult SetAddress(string sectionId, HotkeyModifiers address);

    /// <summary>Повернути типову адресу розділу (найчастіші модифікатори типових клавіш його дій).</summary>
    HotkeyAssignResult ResetAddress(string sectionId);

    /// <summary>Призначити дії клавішу (віртуальний код Windows); null прибирає її. Зайняте поєднання відхиляється.</summary>
    HotkeyAssignResult SetKey(string globalActionId, int? virtualKey);

    /// <summary>Повернути клавішу за замовчуванням з <see cref="Kastom.Sdk.Actions.ModuleAction.DefaultHotkey"/>.</summary>
    HotkeyAssignResult ResetKey(string globalActionId);

    /// <summary>
    /// Тимчасово зняти всі поєднання з реєстрації (поки користувач натискає клавішу на сторінці клавіш:
    /// інакше Windows перехопить уже зареєстроване поєднання раніше, ніж його побачить вікно).
    /// Dispose повертає реєстрацію. Вкладені виклики допустимі.
    /// </summary>
    IDisposable Suspend();

    event EventHandler? Changed;
}
