namespace Kastom.Modules.Audio.Model;

/// <summary>Напрям звуку: вивід (колонки, навушники) чи запис (мікрофони).</summary>
public enum AudioFlow
{
    Render,
    Capture,
}

/// <summary>
/// Ролі пристрою за замовчуванням у Windows. «Звичайний» — Console і Multimedia разом (так їх ставить
/// і сама Windows), «для зв'язку» — Communications (його беруть Discord, Teams, Zoom).
/// </summary>
[Flags]
public enum DefaultRoles
{
    None = 0,
    Console = 1,
    Multimedia = 2,
    Communications = 4,
    Normal = Console | Multimedia,
    All = Console | Multimedia | Communications,
}

/// <summary>Що вміє бекенд на цьому ПК. Обчислюється при запуску й може змінитися, якщо Windows відмовила.</summary>
public sealed record BackendCapabilities(bool DefaultDeviceSwitch, bool PerAppRouting, bool Listen)
{
    public static BackendCapabilities None { get; } = new(false, false, false);
}

/// <summary>Незмінний знімок усього звуку: активні пристрої виводу й запису з їхніми програмами.</summary>
public sealed record AudioSnapshot(IReadOnlyList<AudioDevice> Devices, bool IsAvailable = true)
{
    /// <summary>Порожній знімок; <see cref="IsAvailable"/> = false — звукова служба недоступна.</summary>
    public static AudioSnapshot Empty { get; } = new([], IsAvailable: false);

    public IEnumerable<AudioDevice> Outputs => Devices.Where(d => d.Flow == AudioFlow.Render);

    public IEnumerable<AudioDevice> Inputs => Devices.Where(d => d.Flow == AudioFlow.Capture);

    public AudioDevice? DefaultOutput => Outputs.FirstOrDefault(d => d.IsDefault);

    public AudioDevice? DefaultInput => Inputs.FirstOrDefault(d => d.IsDefault);

    public AudioDevice? FindDevice(string deviceId) => Devices.FirstOrDefault(d => d.Id == deviceId);

    /// <summary>
    /// Програма, чию назву несе пристрій у дужках («Speakers (NVIDIA Broadcast)» → «NVIDIA Broadcast»): збіг без урахування
    /// регістру з <see cref="AudioApp.Name"/> або <see cref="AudioApp.ProcessName"/>. Береться лише програма з відомим
    /// шляхом до exe; null — такої немає.
    /// </summary>
    public AudioApp? FindAppOfDevice(AudioDevice device)
    {
        if (BracketedName(device.Name) is not { } name) return null;

        return AllApps.FirstOrDefault(a =>
            a.ExePath is not null
            && (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.ProcessName, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Текст між першою "(" і останньою ")" назви пристрою, обрізаний; null, якщо дужок немає чи вони порожні.</summary>
    internal static string? BracketedName(string deviceName)
    {
        var open = deviceName.IndexOf('(');
        var close = deviceName.LastIndexOf(')');
        if (open < 0 || close <= open) return null;

        var inner = deviceName[(open + 1)..close].Trim();
        return inner.Length == 0 ? null : inner;
    }

    /// <summary>Програма на будь-якому пристрої виводу.</summary>
    public IEnumerable<AudioApp> AllApps => Outputs.SelectMany(d => d.Apps);
}

/// <summary>Активний пристрій виводу чи запису.</summary>
public sealed record AudioDevice
{
    /// <summary>Ідентифікатор MMDevice, наприклад "{0.0.0.00000000}.{8c1f…}"; стабільний між перезапусками.</summary>
    public required string Id { get; init; }

    public required AudioFlow Flow { get; init; }

    /// <summary>Назва, як у Windows: «Навушники (HyperX Cloud II)».</summary>
    public required string Name { get; init; }

    /// <summary>Шлях до значка пристрою з реєстру Windows ("%windir%\system32\mmres.dll,-3010") або null.</summary>
    public string? IconPath { get; init; }

    /// <summary>Гучність 0..1, як повзунок у Windows (0,45 = 45 %).</summary>
    public required float Volume { get; init; }

    public required bool IsMuted { get; init; }

    /// <summary>Пристрій за замовчуванням (роль Console).</summary>
    public bool IsDefault { get; init; }

    /// <summary>Пристрій «для зв'язку» за замовчуванням (роль Communications).</summary>
    public bool IsDefaultCommunications { get; init; }

    /// <summary>Програми, що мають звуковий сеанс на цьому пристрої. Лише для пристроїв виводу.</summary>
    public IReadOnlyList<AudioApp> Apps { get; init; } = [];

    /// <summary>
    /// Додаткові налаштування мікрофона (підсилення, прослуховування). Null — ще не запитано
    /// (<see cref="IAudioBackend.RequestMicDetailsAsync"/>) або це пристрій виводу.
    /// </summary>
    public MicDetails? Details { get; init; }
}

/// <summary>
/// Програма на пристрої виводу: усі звукові сеанси одного виконуваного файлу (наприклад, кілька процесів Chrome)
/// або «Системні звуки». Гучність і вимкнення діють на всі її сеанси разом.
/// </summary>
public sealed record AudioApp
{
    /// <summary>
    /// Ключ у межах пристрою, стабільний, поки програма звучить: "system" для системних звуків,
    /// повний шлях до exe в нижньому регістрі, або "pid:1234", якщо шлях невідомий.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Пристрій, на якому зараз звучить програма.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Назва для людей: «Google Chrome», «Discord», «Системні звуки».</summary>
    public required string Name { get; init; }

    /// <summary>Звідки взяти значок: шлях до exe або шлях зі звукового сеансу ("файл,-індекс"). Null — значка немає.</summary>
    public string? IconPath { get; init; }

    public required float Volume { get; init; }

    public required bool IsMuted { get; init; }

    public bool IsSystemSounds { get; init; }

    /// <summary>Ім'я процесу без ".exe" у нижньому регістрі ("discord"); для системних звуків — null.</summary>
    public string? ProcessName { get; init; }

    public IReadOnlyList<int> ProcessIds { get; init; } = [];

    /// <summary>Хоча б один сеанс зараз відтворює звук (а не просто відкритий).</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Пристрій, на який програму направлено вручну (З7, експериментально). Null — пристрій за замовчуванням
    /// або бекенд цього не вміє.
    /// </summary>
    public string? RoutedDeviceId { get; init; }

    /// <summary>
    /// Повний шлях до exe, якщо він відомий: ключ програми — це шлях (а не "system" чи "pid:…").
    /// У «Системних звуків» і програм, чий шлях не вдалося взнати, — null.
    /// </summary>
    public string? ExePath =>
        !IsSystemSounds && System.IO.Path.IsPathFullyQualified(Key) && Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Key
            : null;
}

/// <summary>Налаштування мікрофона, які бекенд читає лише на запит (вони звертаються до драйвера).</summary>
public sealed record MicDetails
{
    /// <summary>Підсилення мікрофона; null — драйвер його не дає.</summary>
    public MicBoost? Boost { get; init; }

    /// <summary>Чи вміє бекенд «прослуховувати цей пристрій».</summary>
    public bool ListenSupported { get; init; }

    /// <summary>«Прослуховувати цей пристрій» увімкнено.</summary>
    public bool ListenEnabled { get; init; }

    /// <summary>Куди виводити прослуховування; null — пристрій виводу за замовчуванням.</summary>
    public string? ListenTargetId { get; init; }
}

/// <summary>Підсилення мікрофона в децибелах: поточне значення, межі й крок драйвера.</summary>
public sealed record MicBoost(float LevelDb, float MinDb, float MaxDb, float StepDb);

/// <summary>Миттєві рівні звуку 0..1. Ключі — <see cref="PeakKeys"/>.</summary>
public sealed record PeakSample(IReadOnlyDictionary<string, float> Values)
{
    public static PeakSample Empty { get; } = new(new Dictionary<string, float>());

    public float Get(string key) => Values.TryGetValue(key, out var value) ? value : 0f;
}

/// <summary>Ключі рівнів у <see cref="PeakSample"/>.</summary>
public static class PeakKeys
{
    public static string Device(string deviceId) => "dev|" + deviceId;

    public static string App(string deviceId, string appKey) => "app|" + deviceId + "|" + appKey;
}
