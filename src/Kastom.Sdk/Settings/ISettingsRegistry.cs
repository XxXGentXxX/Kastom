using System.Windows;

namespace Kastom.Sdk.Settings;

/// <summary>
/// Налаштування одного модуля. Модуль описує їх у <see cref="IKastomModule.Configure"/>,
/// лаунчер зберігає значення у файл і показує їх в інтерфейсі.
/// </summary>
public interface ISettingsRegistry
{
    /// <summary>Додати опис налаштування. Ключі в межах модуля мають бути унікальні.</summary>
    void Define(SettingDefinition definition);

    /// <summary>Усі описи в порядку додавання.</summary>
    IReadOnlyList<SettingDefinition> Definitions { get; }

    bool GetBool(string key);

    double GetDouble(string key);

    string GetString(string key);

    /// <summary>Записати значення перемикача.</summary>
    void Set(string key, bool value);

    /// <summary>Записати значення повзунка; лаунчер обріже його до Min..Max.</summary>
    void Set(string key, double value);

    /// <summary>Записати текст або вибір; для вибору значення мусить бути одним з варіантів.</summary>
    void Set(string key, string value);

    /// <summary>Значення змінилося — з інтерфейсу лаунчера чи з коду модуля.</summary>
    event EventHandler<SettingChangedEventArgs>? Changed;

    /// <summary>
    /// Власна сторінка для складного інтерфейсу (наприклад, мікшер звуку). Фабрику лаунчер викликає
    /// в потоці інтерфейсу щоразу, коли сторінку відкривають. Описані налаштування показуються під нею.
    /// </summary>
    void SetCustomPage(Func<FrameworkElement> factory);

    Func<FrameworkElement>? CustomPageFactory { get; }
}

public sealed class SettingChangedEventArgs(string key, object value) : EventArgs
{
    public string Key { get; } = key;

    /// <summary>bool, double або string.</summary>
    public object Value { get; } = value;
}
