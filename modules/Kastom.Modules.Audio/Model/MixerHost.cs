namespace Kastom.Modules.Audio.Model;

/// <summary>Усе, що потрібно мікшеру й значкам: бекенд звуку, налаштування, переходи в Windows.</summary>
public sealed class MixerHost(IAudioBackend backend, AudioOptions options, IAudioShell shell)
{
    public IAudioBackend Backend { get; } = backend;

    public AudioOptions Options { get; } = options;

    public IAudioShell Shell { get; } = shell;
}

/// <summary>Переходи на сторінки Windows і повідомлення про помилки. Справжня реалізація — Shell/AudioShell.</summary>
public interface IAudioShell
{
    /// <summary>Параметри → Система → Звук.</summary>
    void OpenSoundSettings();

    /// <summary>Властивості мікрофона за замовчуванням (шумозаглушення, формат).</summary>
    void OpenInputProperties();

    /// <summary>Властивості пристрою виводу за замовчуванням.</summary>
    void OpenOutputProperties();

    /// <summary>Параметри → Звук → Мікшер гучності (вибір пристрою для програм у самій Windows).</summary>
    void OpenAppVolumeSettings();

    /// <summary>Параметри → Персоналізація → Панель задач (значки, які видно біля годинника).</summary>
    void OpenTaskbarSettings();

    /// <summary>Записати помилку в лог і коротко сказати про неї користувачеві підказкою на екрані.</summary>
    void ReportError(string userMessage, Exception? exception);
}
