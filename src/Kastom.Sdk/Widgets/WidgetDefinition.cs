using System.Windows;

namespace Kastom.Sdk.Widgets;

/// <summary>Віджет модуля. Лаунчер сам створює для нього вікно, рамку, перетягування й збереження місця.</summary>
public sealed class WidgetDefinition
{
    /// <summary>Ідентифікатор у межах модуля.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public double DefaultWidth { get; init; } = 280;

    public double DefaultHeight { get; init; } = 160;

    /// <summary>Створює вміст віджета. Викликається в потоці інтерфейсу щоразу, коли віджет показують.</summary>
    public required Func<FrameworkElement> CreateContent { get; init; }
}

public interface IWidgetRegistry
{
    void Register(WidgetDefinition widget);

    IReadOnlyList<WidgetDefinition> Widgets { get; }
}
