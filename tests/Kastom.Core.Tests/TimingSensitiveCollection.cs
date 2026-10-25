namespace Kastom.Core.Tests;

/// <summary>
/// Тести, що міряють час. xUnit виконує продовження async-тестів на своїх робочих потоках (стільки, скільки ядер);
/// на двоядерній машині GitHub Actions сусідні тести, які блокують потік (запуск kastom-catalog, очікування каналу),
/// затримували ці продовження на секунди. Колекція виконується окремо, після паралельних тестів.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveCollection
{
    public const string Name = "Чутливі до часу";
}
