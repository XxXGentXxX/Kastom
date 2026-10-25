using System.Text.RegularExpressions;
using Kastom.Sdk;

namespace Kastom.Core.Models;

public enum ModuleRunMode
{
    /// <summary>Плагін у процесі лаунчера (більшість модулів).</summary>
    InProcess,

    /// <summary>Окремий процес під наглядом лаунчера (шпалери, панель задач). Поки не підтримується.</summary>
    OutOfProcess,
}

/// <summary>Опис модуля, що лежить у корені його пакета як module.json.</summary>
public sealed partial class ModuleManifest
{
    public const string FileName = "module.json";

    /// <summary>Латиниця в нижньому регістрі, цифри, дефіси: "audio", "desktop-widgets".</summary>
    public required string Id { get; init; }

    /// <summary>Назва для людей, українською.</summary>
    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>SemVer, див. <see cref="ModuleVersion"/>.</summary>
    public required string Version { get; init; }

    /// <summary><see cref="KastomSdk.ContractVersion"/>, під який модуль зібрано.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>Ім'я головної збірки модуля, наприклад "Kastom.Modules.Audio.dll".</summary>
    public required string EntryAssembly { get; init; }

    /// <summary>Повне ім'я класу, що реалізує <see cref="IKastomModule"/>.</summary>
    public required string EntryType { get; init; }

    public string MinLauncherVersion { get; init; } = "0.1.0";

    public ModuleRunMode RunMode { get; init; } = ModuleRunMode.InProcess;

    public string? Author { get; init; }

    /// <summary>
    /// Значок модуля: шлях до PNG у пакеті, відносний, зі слешами "/" ("icon.png", "assets/icon.png"), без ".." і кореня,
    /// файл до 32 КБ. Квадратний бажано (64 чи 128 px); розмір у пікселях ніхто не перевіряє. Правила — <see cref="Catalog.ModuleIcons"/>.
    /// </summary>
    public string? Icon { get; init; }

    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= 40 } && id != KastomSdk.LauncherModuleId && IdPattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
