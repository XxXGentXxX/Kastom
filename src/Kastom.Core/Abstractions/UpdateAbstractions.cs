namespace Kastom.Core.Abstractions;

/// <summary>Оновлення самого лаунчера. Реалізація в проєкті лаунчера через Velopack.</summary>
public interface ILauncherUpdater
{
    /// <summary>False, коли лаунчер запущено зі збірки, а не встановлено інсталятором: оновлювати нічого.</summary>
    bool IsInstalled { get; }

    /// <summary>Нова версія, якщо є; інакше null.</summary>
    Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken);

    /// <summary>Завантажити знайдене оновлення, застосувати й перезапустити лаунчер.</summary>
    Task DownloadAndRestartAsync(CancellationToken cancellationToken);
}

/// <summary>Перевірка оновлень лаунчера й модулів: при старті й раз на годину.</summary>
public interface IUpdateService : IDisposable
{
    DateTimeOffset? LastCheck { get; }

    bool IsChecking { get; }

    string? LauncherAvailableVersion { get; }

    /// <summary>Ідентифікатори модулів, для яких є сумісне оновлення.</summary>
    IReadOnlyList<string> ModulesWithUpdates { get; }

    /// <summary>Запустити фоновий цикл: перша перевірка невдовзі після старту, далі з інтервалом.</summary>
    void Start();

    Task CheckNowAsync(CancellationToken cancellationToken = default);

    Task ApplyLauncherUpdateAsync(CancellationToken cancellationToken = default);

    event EventHandler? StateChanged;
}
