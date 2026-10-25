namespace Kastom.Modules.Audio.Model;

/// <summary>
/// Звук Windows для решти модуля. Справжній бекенд — Core Audio через COM (CoreAudio/CoreAudioBackend),
/// для тестів і знімків — <see cref="Fake.FakeAudioBackend"/>.
/// <para>
/// Потоки: події <see cref="Changed"/> і <see cref="Peaks"/> приходять з потоку бекенда, не з потоку інтерфейсу.
/// Команди можна викликати з будь-якого потоку; їхня задача завершується, коли зміну застосовано
/// і <see cref="Current"/> уже її показує. Помилка Windows — виняток <see cref="AudioCommandException"/> у задачі.
/// </para>
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>Останній знімок. До <see cref="StartAsync"/> — <see cref="AudioSnapshot.Empty"/>.</summary>
    AudioSnapshot Current { get; }

    BackendCapabilities Capabilities { get; }

    /// <summary>Знімок змінився: пристрої, програми, гучність, пристрій за замовчуванням. Події згруповано (не частіше ~20 на секунду).</summary>
    event EventHandler<AudioSnapshot>? Changed;

    /// <summary>Нові рівні звуку; приходять лише поки відкрито хоч одне <see cref="BeginMetering"/> (≈30 на секунду).</summary>
    event EventHandler<PeakSample>? Peaks;

    /// <summary>Почати роботу: перелічити пристрої й сеанси, підписатися на зміни. Повторний виклик після <see cref="StopAsync"/> дозволений.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Відписатися від Windows і звільнити все. Після цього <see cref="Current"/> = <see cref="AudioSnapshot.Empty"/>.</summary>
    Task StopAsync();

    Task SetDeviceVolumeAsync(string deviceId, float volume);

    Task SetDeviceMuteAsync(string deviceId, bool muted);

    Task SetAppVolumeAsync(string deviceId, string appKey, float volume);

    Task SetAppMuteAsync(string deviceId, string appKey, bool muted);

    /// <summary>Зробити пристрій пристроєм за замовчуванням у вказаних ролях (З5, З8).</summary>
    Task SetDefaultDeviceAsync(string deviceId, DefaultRoles roles);

    /// <summary>Направити програму на пристрій виводу (З7, експериментально); null — назад на пристрій за замовчуванням.</summary>
    Task SetAppOutputAsync(string deviceId, string appKey, string? targetDeviceId);

    /// <summary>Скинути ручний вибір виводу для всіх програм.</summary>
    Task ResetAppOutputsAsync();

    /// <summary>Прочитати підсилення й прослуховування мікрофона; результат з'являється в <see cref="AudioDevice.Details"/>.</summary>
    Task RequestMicDetailsAsync(string captureDeviceId);

    /// <summary>«Прослуховувати цей пристрій»; targetDeviceId null — пристрій виводу за замовчуванням.</summary>
    Task SetListenAsync(string captureDeviceId, bool enabled, string? targetDeviceId);

    /// <summary>Підсилення мікрофона в дБ; бекенд округлить до кроку драйвера.</summary>
    Task SetBoostAsync(string captureDeviceId, float levelDb);

    /// <summary>
    /// Почати вимірювати рівні звуку (поки мікшер на екрані). Повертає «квиток»: Dispose закінчує вимірювання.
    /// Квитки рахуються: рівні вимірюються, поки відкритий хоча б один.
    /// <para>
    /// microphone = true — ще й рівень мікрофона за замовчуванням. Windows рахує його лише тоді, коли мікрофон
    /// хтось записує, тож бекенд на цей час сам відкриває мікрофон (Windows показує «використовує мікрофон»,
    /// Bluetooth-гарнітура перейшла б у режим дзвінка — її бекенд не відкриває). Просити лише тоді, коли індикатор
    /// мікрофона справді на екрані.
    /// </para>
    /// </summary>
    IDisposable BeginMetering(bool microphone = false);
}

/// <summary>Windows відмовила виконати команду. <see cref="Exception.Message"/> — технічний текст для логу.</summary>
public sealed class AudioCommandException(string message, Exception? inner = null) : Exception(message, inner);
