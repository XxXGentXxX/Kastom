using Kastom.Core.Models;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Core.Abstractions;

/// <summary>Стан лаунчера (launcher.json): читання при старті, запис після кожної зміни.</summary>
public interface ILauncherStateStore
{
    LauncherState State { get; }

    /// <summary>Записати стан на диск атомарно (тимчасовий файл + заміна).</summary>
    void Save();

    event EventHandler? Saved;
}

/// <summary>Дія, зареєстрована лаунчером або увімкненим модулем.</summary>
public sealed record RegisteredAction(string ModuleId, string ModuleName, ModuleAction Action)
{
    /// <summary>"&lt;moduleId&gt;/&lt;actionId&gt;", наприклад "audio/mute-mic".</summary>
    public string GlobalId => $"{ModuleId}/{Action.Id}";
}

/// <summary>Усі дії лаунчера й увімкнених модулів — для клавіш, кругового меню й віджетів.</summary>
public interface IActionCatalog
{
    IReadOnlyList<RegisteredAction> All { get; }

    RegisteredAction? Find(string globalId);

    /// <summary>Виконати дію в потоці інтерфейсу; помилку записати в лог і не пропускати далі.</summary>
    Task ExecuteAsync(string globalId, ActionTrigger trigger);

    /// <summary>Власні дії лаунчера (модуль <see cref="Kastom.Sdk.KastomSdk.LauncherModuleId"/>).</summary>
    void RegisterLauncherAction(ModuleAction action);

    /// <summary>Набір дій змінився: модуль увімкнули чи вимкнули.</summary>
    event EventHandler? Changed;
}

/// <summary>Стан одного модуля для інтерфейсу: встановлені й доступні в каталозі разом.</summary>
public sealed class ModuleInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public string? InstalledVersion { get; init; }

    /// <summary>Версія в каталозі.</summary>
    public string? AvailableVersion { get; init; }

    public string? PreviousVersion { get; init; }

    public bool IsInstalled => InstalledVersion is not null;

    public bool IsEnabled { get; init; }

    public bool IsRunning { get; init; }

    /// <summary>У каталозі новіша сумісна версія.</summary>
    public bool HasUpdate { get; init; }

    /// <summary>Версія з каталогу підходить цьому лаунчеру (контракт і мінімальна версія).</summary>
    public bool IsCompatible { get; init; } = true;

    /// <summary>Зміна набуде чинності після перезапуску лаунчера.</summary>
    public bool RestartRequired { get; init; }

    /// <summary>Остання помилка: модуль не завантажився чи впав при старті й був вимкнений.</summary>
    public string? Error { get; init; }

    public ModuleRunMode RunMode { get; init; }

    public string? Changelog { get; init; }
}

public sealed record OperationResult(bool Success, string? Error = null, bool RestartRequired = false)
{
    public static OperationResult Ok(bool restartRequired = false) => new(true, null, restartRequired);

    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>Усе про модулі для інтерфейсу лаунчера: каталог, встановлення, вмикання, налаштування.</summary>
public interface IModuleManager
{
    /// <summary>Встановлені модулі й модулі з каталогу, впорядковані за назвою.</summary>
    IReadOnlyList<ModuleInfo> Modules { get; }

    event EventHandler? Changed;

    /// <summary>При старті лаунчера: завантажити й запустити всі увімкнені модулі. Падіння одного не зупиняє інших.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>При виході: зупинити всі запущені модулі (з обмеженням часу на кожен).</summary>
    Task StopAllAsync(CancellationToken cancellationToken);

    Task<OperationResult> InstallAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateAsync(string moduleId, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult> UninstallAsync(string moduleId);

    Task<OperationResult> EnableAsync(string moduleId);

    Task<OperationResult> DisableAsync(string moduleId);

    /// <summary>Повернутися до <see cref="ModuleInfo.PreviousVersion"/>.</summary>
    Task<OperationResult> RollbackAsync(string moduleId);

    /// <summary>Налаштування модуля, якщо його вже налаштовано (Configure) у цьому запуску.</summary>
    ISettingsRegistry? GetSettings(string moduleId);

    IReadOnlyList<WidgetDefinition> GetWidgets(string moduleId);
}
