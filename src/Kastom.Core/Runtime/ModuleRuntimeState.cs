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

    /// <summary>Модуль є в стані, але його файлів немає (наприклад, лаунчер видалили й поставили знову).</summary>
    public bool Missing { get; set; }

    /// <summary>Файли модуля саме завантажуються з каталогу заново.</summary>
    public bool Repairing { get; set; }
}
