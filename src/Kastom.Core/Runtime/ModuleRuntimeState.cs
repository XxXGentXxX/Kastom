namespace Kastom.Core.Runtime;

/// <summary>Що з модулем відбувається в цьому запуску лаунчера (на диск не пишеться).</summary>
internal sealed class ModuleRuntimeState
{
    /// <summary>Версія, збірку якої вже завантажено в процес. Завантажену збірку не вивантажити до перезапуску.</summary>
    public string? LoadedVersion { get; set; }

    /// <summary>Налаштований екземпляр модуля; null, поки не завантажено чи Configure впав.</summary>
    public LoadedModule? Module { get; set; }

    public bool IsRunning { get; set; }

    public string? Error { get; set; }

    public bool RestartRequired { get; set; }
}
