namespace Kastom.Sdk.Settings;

/// <summary>
/// Опис одного налаштування. Лаунчер сам малює для нього елемент керування — і на сторінці модуля,
/// і у вкладці «Налаштування модулів», а з <see cref="IsQuick"/> = true ще й у швидкій панелі та віджеті.
/// </summary>
public abstract class SettingDefinition
{
    /// <summary>Ключ у межах модуля: латиниця, цифри, дефіс. Наприклад "master-volume".</summary>
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Назва групи, у якій налаштування стоїть на сторінці. Null — загальна група.</summary>
    public string? Group { get; init; }

    /// <summary>Показувати у швидкій панелі й віджеті лаунчера.</summary>
    public bool IsQuick { get; init; }
}

/// <summary>Перемикач так/ні.</summary>
public sealed class ToggleSetting : SettingDefinition
{
    public bool Default { get; init; }
}

/// <summary>Повзунок з числом.</summary>
public sealed class RangeSetting : SettingDefinition
{
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 1;
    public double Default { get; init; }

    /// <summary>Одиниця біля числа, наприклад "%".</summary>
    public string? Unit { get; init; }
}

/// <summary>Вибір одного варіанта зі списку.</summary>
public sealed class ChoiceSetting : SettingDefinition
{
    public required IReadOnlyList<ChoiceOption> Options { get; init; }

    /// <summary>Значення (<see cref="ChoiceOption.Value"/>) за замовчуванням.</summary>
    public required string Default { get; init; }
}

public sealed record ChoiceOption(string Value, string Title);

/// <summary>Рядок тексту.</summary>
public sealed class TextSetting : SettingDefinition
{
    public string Default { get; init; } = "";
}

/// <summary>Кнопка, що виконує дію модуля. Значення не зберігає.</summary>
public sealed class ButtonSetting : SettingDefinition
{
    /// <summary>Ідентифікатор дії модуля (<see cref="Actions.ModuleAction.Id"/>).</summary>
    public required string ActionId { get; init; }
}
