namespace Kastom.Sdk;

/// <summary>
/// Точка входу модуля. Лаунчер створює екземпляр класу, указаного в module.json (entryType),
/// один раз викликає <see cref="Configure"/>, а <see cref="StartAsync"/> і <see cref="StopAsync"/> —
/// щоразу, коли модуль вмикають чи вимикають.
/// </summary>
public interface IKastomModule
{
    /// <summary>
    /// Реєстрація налаштувань, дій і віджетів. Викликається один раз за запуск лаунчера, у потоці інтерфейсу,
    /// ще до <see cref="StartAsync"/>. Тут не можна змінювати систему.
    /// </summary>
    void Configure(IModuleContext context);

    /// <summary>
    /// Модуль увімкнено: почати роботу. Викликається у фоновому потоці з обмеженням часу;
    /// для роботи з інтерфейсом — <see cref="IModuleContext.Ui"/>.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Модуль вимикають, лаунчер закривається або Windows завершує сеанс: зупинитися й повернути все,
    /// що модуль змінив у системі. Викликається у фоновому потоці з обмеженням часу (кілька секунд).
    /// Після StopAsync лаунчер може знову викликати StartAsync без повторного Configure.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
