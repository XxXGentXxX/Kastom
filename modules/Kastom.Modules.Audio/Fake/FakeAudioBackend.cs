using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Fake;

/// <summary>
/// Бекенд без справжнього звуку: знімок у пам'яті, команди міняють його й одразу кидають <see cref="Changed"/>
/// у потоці, що викликав команду. Для тестів і знімків сторінок (змінна середовища KASTOM_AUDIO_FAKE=1).
/// </summary>
public sealed class FakeAudioBackend : IAudioBackend
{
    private readonly object _sync = new();
    private readonly AudioSnapshot _initial;
    private readonly Dictionary<string, MicDetails> _micDetails;
    private AudioSnapshot _current = AudioSnapshot.Empty;
    private int _metering;
    private int _micMetering;

    public FakeAudioBackend(
        AudioSnapshot initial,
        BackendCapabilities? capabilities = null,
        IReadOnlyDictionary<string, MicDetails>? micDetails = null)
    {
        _initial = initial;
        Capabilities = capabilities ?? new BackendCapabilities(true, true, true);
        _micDetails = micDetails?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, MicDetails>();
    }

    public AudioSnapshot Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public BackendCapabilities Capabilities { get; set; }

    public bool IsStarted { get; private set; }

    /// <summary>Скільки «квитків» вимірювання зараз відкрито.</summary>
    public int MeteringCount => Volatile.Read(ref _metering);

    /// <summary>Скільки з них просять ще й рівень мікрофона.</summary>
    public int MicMeteringCount => Volatile.Read(ref _micMetering);

    /// <summary>Рівні, які бекенд сам надсилає, щойно відкривається перший квиток вимірювання (для знімків).</summary>
    public PeakSample? AutoPeaks { get; set; }

    /// <summary>Журнал команд у вигляді "SetAppMute dev app True" — для перевірок у тестах.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Якщо задано, наступна команда кине цей виняток і нічого не змінить.</summary>
    public AudioCommandException? FailNext { get; set; }

    public event EventHandler<AudioSnapshot>? Changed;

    public event EventHandler<PeakSample>? Peaks;

    public static FakeAudioBackend CreateDemo() =>
        new(DemoSnapshot(), micDetails: DemoMicDetails()) { AutoPeaks = DemoPeaks() };

    /// <summary>Правдоподібний набір пристроїв і програм для знімків і тестів.</summary>
    public static AudioSnapshot DemoSnapshot()
    {
        const string headphones = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}";
        const string speakers = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}";
        const string monitor = "{0.0.0.00000000}.{33333333-3333-3333-3333-333333333333}";
        const string headsetMic = "{0.0.1.00000000}.{44444444-4444-4444-4444-444444444444}";
        const string webcamMic = "{0.0.1.00000000}.{55555555-5555-5555-5555-555555555555}";

        return new AudioSnapshot(
        [
            new AudioDevice
            {
                Id = headphones,
                Flow = AudioFlow.Render,
                Name = "Навушники (HyperX Cloud II)",
                Volume = 0.45f,
                IsMuted = false,
                IsDefault = true,
                IsDefaultCommunications = true,
                Apps =
                [
                    App(headphones, @"c:\program files\google\chrome\application\chrome.exe", "Google Chrome", "chrome", 0.8f, active: true, pids: [4120, 7712]),
                    App(headphones, @"c:\users\user\appdata\local\discord\app-1.0.9200\discord.exe", "Discord", "discord", 1f, active: true, pids: [9032]),
                    App(headphones, @"c:\program files\steam\steamapps\common\counter-strike global offensive\game\bin\win64\cs2.exe", "Counter-Strike 2", "cs2", 0.6f, muted: true, pids: [15044]),
                    new AudioApp
                    {
                        Key = "system",
                        DeviceId = headphones,
                        Name = "Системні звуки",
                        Volume = 0.5f,
                        IsMuted = false,
                        IsSystemSounds = true,
                    },
                ],
            },
            new AudioDevice
            {
                Id = speakers,
                Flow = AudioFlow.Render,
                Name = "Динаміки (Realtek(R) Audio)",
                Volume = 0.3f,
                IsMuted = false,
                Apps =
                [
                    App(speakers, @"c:\users\user\appdata\roaming\spotify\spotify.exe", "Spotify", "spotify", 0.7f, active: true, pids: [6240], routed: speakers),
                ],
            },
            new AudioDevice
            {
                Id = monitor,
                Flow = AudioFlow.Render,
                Name = "LG ULTRAGEAR (NVIDIA High Definition Audio)",
                Volume = 1f,
                IsMuted = true,
            },
            new AudioDevice
            {
                Id = headsetMic,
                Flow = AudioFlow.Capture,
                Name = "Мікрофон (HyperX Cloud II)",
                Volume = 0.7f,
                IsMuted = false,
                IsDefault = true,
                IsDefaultCommunications = true,
            },
            new AudioDevice
            {
                Id = webcamMic,
                Flow = AudioFlow.Capture,
                Name = "Мікрофон (C922 Pro Stream Webcam)",
                Volume = 0.55f,
                IsMuted = true,
            },
        ]);

        static AudioApp App(
            string deviceId, string path, string name, string process, float volume,
            bool muted = false, bool active = false, int[]? pids = null, string? routed = null) => new()
        {
            Key = path,
            DeviceId = deviceId,
            Name = name,
            IconPath = null,
            Volume = volume,
            IsMuted = muted,
            ProcessName = process,
            ProcessIds = pids ?? [],
            IsActive = active,
            RoutedDeviceId = routed,
        };
    }

    public static IReadOnlyDictionary<string, MicDetails> DemoMicDetails() => new Dictionary<string, MicDetails>
    {
        ["{0.0.1.00000000}.{44444444-4444-4444-4444-444444444444}"] = new()
        {
            Boost = new MicBoost(10, 0, 30, 10),
            ListenSupported = true,
            ListenEnabled = false,
        },
        ["{0.0.1.00000000}.{55555555-5555-5555-5555-555555555555}"] = new()
        {
            ListenSupported = true,
            ListenEnabled = false,
        },
    };

    public static PeakSample DemoPeaks()
    {
        var snapshot = DemoSnapshot();
        var values = new Dictionary<string, float>();
        var level = 0.62f;
        foreach (var device in snapshot.Devices)
        {
            values[PeakKeys.Device(device.Id)] = device.IsMuted ? 0 : level;
            foreach (var app in device.Apps)
                values[PeakKeys.App(device.Id, app.Key)] = app.IsActive && !app.IsMuted ? level * app.Volume : 0;
            level *= 0.7f;
        }

        return new PeakSample(values);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _current = _initial;
            IsStarted = true;
        }

        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            _current = AudioSnapshot.Empty;
            IsStarted = false;
        }

        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync();

    /// <summary>Імітує зміну ззовні (інша програма, Windows): новий знімок і подія.</summary>
    public void Replace(AudioSnapshot snapshot)
    {
        lock (_sync) _current = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    /// <summary>Надіслати рівні, ніби їх виміряв бекенд.</summary>
    public void PushPeaks(PeakSample sample) => Peaks?.Invoke(this, sample);

    public IDisposable BeginMetering(bool microphone = false)
    {
        if (microphone) Interlocked.Increment(ref _micMetering);
        if (Interlocked.Increment(ref _metering) == 1 && AutoPeaks is { } peaks) Peaks?.Invoke(this, peaks);
        return new Ticket(this, microphone);
    }

    public Task SetDeviceVolumeAsync(string deviceId, float volume) =>
        Command($"SetDeviceVolume {deviceId} {volume:0.###}", s => UpdateDevice(s, deviceId, d => d with { Volume = Math.Clamp(volume, 0f, 1f) }));

    public Task SetDeviceMuteAsync(string deviceId, bool muted) =>
        Command($"SetDeviceMute {deviceId} {muted}", s => UpdateDevice(s, deviceId, d => d with { IsMuted = muted }));

    public Task SetAppVolumeAsync(string deviceId, string appKey, float volume) =>
        Command($"SetAppVolume {deviceId} {appKey} {volume:0.###}", s => UpdateApp(s, deviceId, appKey, a => a with { Volume = Math.Clamp(volume, 0f, 1f) }));

    public Task SetAppMuteAsync(string deviceId, string appKey, bool muted) =>
        Command($"SetAppMute {deviceId} {appKey} {muted}", s => UpdateApp(s, deviceId, appKey, a => a with { IsMuted = muted }));

    public Task SetDefaultDeviceAsync(string deviceId, DefaultRoles roles) =>
        Command($"SetDefaultDevice {deviceId} {roles}", s =>
        {
            if (!Capabilities.DefaultDeviceSwitch) throw new AudioCommandException("Перемикання пристрою за замовчуванням недоступне.");
            var target = RequireDevice(s, deviceId);
            var normal = (roles & DefaultRoles.Normal) != 0;
            var communications = (roles & DefaultRoles.Communications) != 0;
            return s with
            {
                Devices = s.Devices.Select(d => d.Flow != target.Flow ? d : d with
                {
                    IsDefault = normal ? d.Id == deviceId : d.IsDefault,
                    IsDefaultCommunications = communications ? d.Id == deviceId : d.IsDefaultCommunications,
                }).ToArray(),
            };
        });

    public Task SetAppOutputAsync(string deviceId, string appKey, string? targetDeviceId) =>
        Command($"SetAppOutput {deviceId} {appKey} {targetDeviceId ?? "default"}", s =>
        {
            if (!Capabilities.PerAppRouting) throw new AudioCommandException("Вивід окремої програми недоступний.");
            var app = RequireDevice(s, deviceId).Apps.FirstOrDefault(a => a.Key == appKey)
                      ?? throw new AudioCommandException($"Програми '{appKey}' на пристрої немає.");
            var destination = targetDeviceId ?? s.DefaultOutput?.Id ?? throw new AudioCommandException("Немає пристрою за замовчуванням.");
            RequireDevice(s, destination);
            return MoveApp(s, app, destination, targetDeviceId);
        });

    public Task ResetAppOutputsAsync() =>
        Command("ResetAppOutputs", s =>
        {
            if (!Capabilities.PerAppRouting) throw new AudioCommandException("Вивід окремої програми недоступний.");
            var defaultId = s.DefaultOutput?.Id;
            foreach (var app in s.AllApps.Where(a => a.RoutedDeviceId is not null).ToList())
                s = defaultId is null ? s : MoveApp(s, app, defaultId, null);
            return s;
        });

    public Task RequestMicDetailsAsync(string captureDeviceId) =>
        Command($"RequestMicDetails {captureDeviceId}", s =>
        {
            var details = _micDetails.TryGetValue(captureDeviceId, out var known) ? known : new MicDetails();
            return UpdateDevice(s, captureDeviceId, d => d with { Details = details });
        });

    public Task SetListenAsync(string captureDeviceId, bool enabled, string? targetDeviceId) =>
        Command($"SetListen {captureDeviceId} {enabled} {targetDeviceId ?? "default"}", s =>
        {
            if (!Capabilities.Listen) throw new AudioCommandException("Прослуховування недоступне.");
            return UpdateDevice(s, captureDeviceId, d =>
            {
                var details = (d.Details ?? new MicDetails()) with { ListenEnabled = enabled, ListenTargetId = targetDeviceId };
                _micDetails[captureDeviceId] = details;
                return d with { Details = details };
            });
        });

    public Task SetBoostAsync(string captureDeviceId, float levelDb) =>
        Command($"SetBoost {captureDeviceId} {levelDb:0.###}", s => UpdateDevice(s, captureDeviceId, d =>
        {
            if (d.Details?.Boost is not { } boost) throw new AudioCommandException("Підсилення недоступне.");
            var steps = boost.StepDb > 0 ? MathF.Round((levelDb - boost.MinDb) / boost.StepDb) : 0;
            var level = Math.Clamp(boost.MinDb + steps * boost.StepDb, boost.MinDb, boost.MaxDb);
            var details = d.Details with { Boost = boost with { LevelDb = level } };
            _micDetails[captureDeviceId] = details;
            return d with { Details = details };
        }));

    private Task Command(string description, Func<AudioSnapshot, AudioSnapshot> apply)
    {
        AudioSnapshot updated;
        lock (_sync)
        {
            Commands.Add(description);
            if (FailNext is { } failure)
            {
                FailNext = null;
                return Task.FromException(failure);
            }

            if (!IsStarted) return Task.FromException(new AudioCommandException("Бекенд не запущено."));
            try
            {
                updated = apply(_current);
            }
            catch (AudioCommandException ex)
            {
                return Task.FromException(ex);
            }

            _current = updated;
        }

        Changed?.Invoke(this, updated);
        return Task.CompletedTask;
    }

    private static AudioDevice RequireDevice(AudioSnapshot snapshot, string deviceId) =>
        snapshot.FindDevice(deviceId) ?? throw new AudioCommandException($"Пристрою '{deviceId}' немає.");

    private static AudioSnapshot UpdateDevice(AudioSnapshot snapshot, string deviceId, Func<AudioDevice, AudioDevice> update)
    {
        RequireDevice(snapshot, deviceId);
        return snapshot with { Devices = snapshot.Devices.Select(d => d.Id == deviceId ? update(d) : d).ToArray() };
    }

    private static AudioSnapshot UpdateApp(AudioSnapshot snapshot, string deviceId, string appKey, Func<AudioApp, AudioApp> update) =>
        UpdateDevice(snapshot, deviceId, d =>
        {
            if (d.Apps.All(a => a.Key != appKey)) throw new AudioCommandException($"Програми '{appKey}' на пристрої немає.");
            return d with { Apps = d.Apps.Select(a => a.Key == appKey ? update(a) : a).ToArray() };
        });

    private static AudioSnapshot MoveApp(AudioSnapshot snapshot, AudioApp app, string destinationId, string? routedId)
    {
        var moved = app with { DeviceId = destinationId, RoutedDeviceId = routedId };
        return snapshot with
        {
            Devices = snapshot.Devices.Select(d =>
            {
                var apps = d.Apps.Where(a => !(d.Id == app.DeviceId && a.Key == app.Key)).ToList();
                if (d.Id == destinationId) apps.Add(moved);
                return d with { Apps = apps };
            }).ToArray(),
        };
    }

    private sealed class Ticket(FakeAudioBackend owner, bool microphone) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (microphone) Interlocked.Decrement(ref owner._micMetering);
            Interlocked.Decrement(ref owner._metering);
        }
    }
}
