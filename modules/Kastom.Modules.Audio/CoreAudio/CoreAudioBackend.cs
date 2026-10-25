using System.Diagnostics;
using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>
/// Звук Windows через Core Audio (WASAPI/MMDevice): пристрої, гучність, сеанси програм, рівні.
/// Усі об'єкти COM живуть в <see cref="AudioThread"/>; зворотні виклики Windows лише ставлять роботу в його чергу.
/// Знімок для решти модуля збирається там же й публікується не частіше ніж раз на <see cref="PublishDelayMs"/>.
/// </summary>
public sealed class CoreAudioBackend : IAudioBackend
{
    private const int PublishDelayMs = 30;
    private const int DevicesRefreshDelayMs = 150;
    private const int MeterIntervalMs = 33;
    private const int MaxRecoveryDelayMs = 30_000;

    private readonly IModuleLogger _log;
    private readonly object _lifecycle = new();
    private readonly Guid _eventContext = Guid.NewGuid();

    // Стан нижче — лише в потоці звуку.
    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _routedByApp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Process> _watchedProcesses = new();
    private List<string> _order = [];
    private IMMDeviceEnumerator? _enumerator;
    private NotificationClient? _notifications;
    private PolicyConfig? _policy;
    private AppRouting? _routing;
    private string? _defaultRender;
    private string? _defaultRenderCommunications;
    private string? _defaultCapture;
    private string? _defaultCaptureCommunications;
    private bool _wanted;
    private bool _alive;
    private bool _publishScheduled;
    private bool _devicesRefreshScheduled;
    private int _recoveryAttempt;
    private long _sessionSequence;
    private Timer? _meterTimer;
    private int _meterPending;
    private CaptureMeterStream? _captureMeter;

    private AudioThread? _thread;
    private volatile AudioSnapshot _current = AudioSnapshot.Empty;
    private volatile BackendCapabilities _capabilities = BackendCapabilities.None;
    private int _meteringCount;
    private int _micMeteringCount;

    public CoreAudioBackend(IModuleLogger log)
    {
        _log = log;
    }

    public AudioSnapshot Current => _current;

    public BackendCapabilities Capabilities => _capabilities;

    public event EventHandler<AudioSnapshot>? Changed;

    public event EventHandler<PeakSample>? Peaks;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle) _thread ??= new AudioThread("Kastom.Audio", _log);
        return _thread.InvokeAsync(() =>
        {
            _wanted = true;
            if (!_alive) TryCreate();
        });
    }

    public Task StopAsync()
    {
        AudioThread? thread;
        lock (_lifecycle) thread = _thread;
        if (thread is null) return Task.CompletedTask;
        return thread.InvokeAsync(() =>
        {
            _wanted = false;
            TearDown();
            Publish();
        });
    }

    public void Dispose()
    {
        AudioThread? thread;
        lock (_lifecycle)
        {
            thread = _thread;
            _thread = null;
        }

        if (thread is null) return;
        try
        {
            thread.InvokeAsync(() =>
            {
                _wanted = false;
                TearDown();
            }).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _log.Warn($"Зупинка звуку: {ex.Message}");
        }

        thread.Dispose();
    }

    // ---- Команди ----

    public Task SetDeviceVolumeAsync(string deviceId, float volume) => Command("гучність пристрою", () =>
    {
        var device = RequireDevice(deviceId);
        var level = Math.Clamp(volume, 0f, 1f);
        var context = _eventContext;
        Com.Check(device.Volume!.SetMasterVolumeLevelScalar(level, ref context), "SetMasterVolumeLevelScalar");
        device.Level = level;
    });

    public Task SetDeviceMuteAsync(string deviceId, bool muted) => Command("вимкнення пристрою", () =>
    {
        var device = RequireDevice(deviceId);
        var context = _eventContext;
        Com.Check(device.Volume!.SetMute(muted, ref context), "SetMute");
        device.Muted = muted;
    });

    public Task SetAppVolumeAsync(string deviceId, string appKey, float volume) => Command("гучність програми", () =>
    {
        var level = Math.Clamp(volume, 0f, 1f);
        foreach (var session in RequireApp(deviceId, appKey))
        {
            if (session.Simple is null) continue;
            var context = _eventContext;
            Com.Check(session.Simple.SetMasterVolume(level, ref context), "SetMasterVolume");
            session.Volume = level;
        }
    });

    public Task SetAppMuteAsync(string deviceId, string appKey, bool muted) => Command("вимкнення програми", () =>
    {
        foreach (var session in RequireApp(deviceId, appKey))
        {
            if (session.Simple is null) continue;
            var context = _eventContext;
            Com.Check(session.Simple.SetMute(muted, ref context), "SetMute");
            session.Muted = muted;
        }
    });

    public Task SetDefaultDeviceAsync(string deviceId, DefaultRoles roles) => Command("пристрій за замовчуванням", () =>
    {
        RequireDevice(deviceId);
        var policy = _policy ?? throw new AudioCommandException("IPolicyConfig недоступний.");
        if (roles.HasFlag(DefaultRoles.Console)) Com.Check(policy.SetDefaultEndpoint(deviceId, ERole.Console), "SetDefaultEndpoint(Console)");
        if (roles.HasFlag(DefaultRoles.Multimedia)) Com.Check(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia), "SetDefaultEndpoint(Multimedia)");
        if (roles.HasFlag(DefaultRoles.Communications)) Com.Check(policy.SetDefaultEndpoint(deviceId, ERole.Communications), "SetDefaultEndpoint(Communications)");
        RefreshDefaults();
    });

    public Task SetAppOutputAsync(string deviceId, string appKey, string? targetDeviceId) => Command("вивід програми", () =>
    {
        var routing = RequireRouting();
        if (targetDeviceId is not null && RequireDevice(targetDeviceId).Flow != EDataFlow.Render)
            throw new AudioCommandException("Програму можна направити лише на пристрій виводу.");

        var processIds = RequireApp(deviceId, appKey).Select(s => s.ProcessId).Where(pid => pid > 0).Distinct().ToList();
        if (processIds.Count == 0) throw new AudioCommandException("У програми немає процесу, який можна направити.");

        var packed = targetDeviceId is null ? null : DeviceIds.Pack(targetDeviceId, EDataFlow.Render);
        foreach (var processId in processIds)
        {
            Com.Check(routing.SetPersisted(processId, EDataFlow.Render, ERole.Console, packed), "SetPersistedDefaultAudioEndpoint(Console)");
            Com.Check(routing.SetPersisted(processId, EDataFlow.Render, ERole.Multimedia, packed), "SetPersistedDefaultAudioEndpoint(Multimedia)");
        }

        _routedByApp[appKey] = targetDeviceId;
    });

    public Task ResetAppOutputsAsync() => Command("скидання виводу програм", () =>
    {
        Com.Check(RequireRouting().ClearAll(), "ClearAllPersistedApplicationDefaultEndpoints");
        _routedByApp.Clear();
    });

    public Task RequestMicDetailsAsync(string captureDeviceId) => Command("налаштування мікрофона", () =>
    {
        var device = RequireDevice(captureDeviceId);
        if (device.Flow != EDataFlow.Capture) throw new AudioCommandException("Це не мікрофон.");
        if (!device.BoostSearched)
        {
            device.BoostSearched = true;
            device.Boost = MicTopology.FindBoost(device.Device);
        }

        ReadMicDetails(device);
    });

    public Task SetListenAsync(string captureDeviceId, bool enabled, string? targetDeviceId) => Command("прослуховування", () =>
    {
        var device = RequireDevice(captureDeviceId);
        if (!_capabilities.Listen || _policy is null) throw new AudioCommandException("Прослуховування недоступне.");
        if (targetDeviceId is not null && RequireDevice(targetDeviceId).Flow != EDataFlow.Render)
            throw new AudioCommandException("Прослуховувати можна лише через пристрій виводу.");

        var target = new PropVariant { ValueType = PropVariant.VtLpwstr, Value1 = Marshal.StringToCoTaskMemUni(targetDeviceId ?? "") };
        try
        {
            Com.Check(_policy.SetPropertyValue(captureDeviceId, PropertyKeys.ListenTarget, ref target), "SetPropertyValue(ListenTarget)");
        }
        finally
        {
            NativeMethods.PropVariantClear(ref target);
        }

        var flag = PropVariant.FromBool(enabled);
        Com.Check(_policy.SetPropertyValue(captureDeviceId, PropertyKeys.ListenEnabled, ref flag), "SetPropertyValue(ListenEnabled)");
        ReadMicDetails(device);
    });

    public Task SetBoostAsync(string captureDeviceId, float levelDb) => Command("підсилення", () =>
    {
        var device = RequireDevice(captureDeviceId);
        var boost = device.Boost ?? throw new AudioCommandException("Драйвер не дає підсилення.");
        var context = _eventContext;
        Com.Check(boost.Set(levelDb, ref context), "SetLevelUniform");
        ReadMicDetails(device);
    });

    public IDisposable BeginMetering(bool microphone = false)
    {
        if (microphone && Interlocked.Increment(ref _micMeteringCount) == 1) Post(ReopenCaptureMeter);
        if (Interlocked.Increment(ref _meteringCount) == 1) Post(StartMetering);
        return new MeteringTicket(this, microphone);
    }

    private void EndMetering(bool microphone)
    {
        if (microphone && Interlocked.Decrement(ref _micMeteringCount) == 0) Post(ReopenCaptureMeter);
        if (Interlocked.Decrement(ref _meteringCount) == 0) Post(StopMetering);
    }

    private Task Command(string what, Action action)
    {
        AudioThread? thread;
        lock (_lifecycle) thread = _thread;
        if (thread is null) return Task.FromException(new AudioCommandException("Звук ще не запущено."));

        return thread.InvokeAsync(() =>
        {
            if (!_alive) throw new AudioCommandException("Звукова служба Windows недоступна.");
            try
            {
                action();
            }
            catch (Exception ex) when (Com.IsServiceGone(ex))
            {
                OnDisconnected(ex);
                throw new AudioCommandException($"{what}: звукова служба зникла.", ex);
            }
            catch (Exception ex) when (Com.IsDeviceGone(ex))
            {
                // Пристрій зник між переліком і командою (висмикнули USB): перелічимо пристрої знову, служба жива.
                ScheduleDevicesRefresh();
                throw new AudioCommandException($"{what}: пристрою вже немає.", ex);
            }
            catch (COMException ex)
            {
                throw new AudioCommandException($"{what}: 0x{ex.HResult:X8}", ex);
            }
            catch (InvalidCastException ex)
            {
                throw new AudioCommandException($"{what}: {ex.Message}", ex);
            }

            // Команда завершується, коли Current уже показує зміну, а подію Changed групуємо з рештою:
            // під час перетягування повзунка команди йдуть частіше, ніж мікшеру треба перемальовуватися.
            _current = BuildSnapshot();
            MarkDirty();
        });
    }

    private DeviceEntry RequireDevice(string deviceId) =>
        _devices.TryGetValue(deviceId, out var device) && device.Volume is not null
            ? device
            : throw new AudioCommandException($"Пристрою {deviceId} немає.");

    private List<SessionEntry> RequireApp(string deviceId, string appKey)
    {
        var sessions = RequireDevice(deviceId).Sessions.Values.Where(s => s.AppKey == appKey).ToList();
        return sessions.Count > 0 ? sessions : throw new AudioCommandException($"Програми {appKey} на пристрої немає.");
    }

    private AppRouting RequireRouting() =>
        _capabilities.PerAppRouting && _routing is not null
            ? _routing
            : throw new AudioCommandException("Вивід окремої програми недоступний.");

    // ---- Життєвий цикл (потік звуку) ----

    private void Post(Action action)
    {
        AudioThread? thread;
        lock (_lifecycle) thread = _thread;
        thread?.Post(action);
    }

    private void Schedule(Action action, int delayMs)
    {
        AudioThread? thread;
        lock (_lifecycle) thread = _thread;
        thread?.Schedule(action, delayMs);
    }

    private void TryCreate()
    {
        try
        {
            Create();
            _recoveryAttempt = 0;
        }
        catch (Exception ex)
        {
            _log.Error("Не вдалося під'єднатися до звукової служби Windows.", ex);
            TearDown();
            Publish();
            ScheduleRecovery();
        }
    }

    private void Create()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _policy = PolicyConfig.TryCreate(_log);
        _routing = PolicyAbi.AppRoutingVerified ? AppRouting.TryCreate(_log) : null;
        _capabilities = new BackendCapabilities(
            DefaultDeviceSwitch: _policy is not null,
            PerAppRouting: _routing is not null,
            Listen: _policy is not null && PolicyAbi.PropertyArgs != PropertyArgs.Unknown);

        RefreshDefaults();
        RefreshDevices();

        _notifications = new NotificationClient(this);
        var hr = _enumerator.RegisterEndpointNotificationCallback(_notifications);
        if (hr < 0) _log.Warn($"Підписка на зміни пристроїв не вдалася (0x{hr:X8}).");

        _alive = true;
        if (Volatile.Read(ref _meteringCount) > 0) StartMetering();
        Publish();
        _log.Info($"Звук: {_devices.Count} пристроїв, перемикання за замовчуванням {(_policy is null ? "недоступне" : "є")}, вивід програм {(_routing is null ? "недоступний" : "є")}.");
    }

    private void TearDown()
    {
        _alive = false;
        StopMeteringCore();

        if (_enumerator is not null && _notifications is not null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notifications);
            }
            catch (Exception)
            {
                // Служба вже зникла — відписуватися нема від кого.
            }
        }

        _notifications = null;
        foreach (var device in _devices.Values.ToList()) DisposeDevice(device);
        _devices.Clear();
        _order = [];
        foreach (var process in _watchedProcesses.Values) process.Dispose();
        _watchedProcesses.Clear();
        _routedByApp.Clear();
        _policy?.Dispose();
        _policy = null;
        _routing?.Dispose();
        _routing = null;
        Com.Release(_enumerator);
        _enumerator = null;
        _defaultRender = _defaultRenderCommunications = _defaultCapture = _defaultCaptureCommunications = null;
        _capabilities = BackendCapabilities.None;
    }

    private void OnDisconnected(Exception? reason)
    {
        if (!_alive) return;
        _log.Warn($"Звукова служба Windows зникла ({reason?.Message}); під'єднаюся знову.");
        TearDown();
        Publish();
        ScheduleRecovery();
    }

    private void ScheduleRecovery()
    {
        if (!_wanted) return;
        var delay = (int)Math.Min(MaxRecoveryDelayMs, 1000 * Math.Pow(2, Math.Min(_recoveryAttempt, 5)));
        _recoveryAttempt++;
        Schedule(() =>
        {
            if (_wanted && !_alive) TryCreate();
        }, delay);
    }

    /// <summary>Будь-яка робота від Windows: помилку зникнення служби перетворити на відновлення, решту записати в лог.</summary>
    private void Guarded(string what, Action action)
    {
        if (!_alive) return;
        try
        {
            action();
        }
        catch (Exception ex) when (Com.IsServiceGone(ex))
        {
            OnDisconnected(ex);
        }
        catch (Exception ex) when (Com.IsDeviceGone(ex))
        {
            _log.Info($"Звук ({what}): пристрій зник, перелічую знову.");
            ScheduleDevicesRefresh();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or AudioCommandException)
        {
            _log.Warn($"Звук ({what}): {ex.Message}");
        }
    }

    // ---- Пристрої ----

    private void RefreshDefaults()
    {
        _defaultRender = DefaultId(EDataFlow.Render, ERole.Console);
        _defaultRenderCommunications = DefaultId(EDataFlow.Render, ERole.Communications);
        _defaultCapture = DefaultId(EDataFlow.Capture, ERole.Console);
        _defaultCaptureCommunications = DefaultId(EDataFlow.Capture, ERole.Communications);
    }

    private string? DefaultId(EDataFlow flow, ERole role)
    {
        if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(flow, role, out var device) < 0 || device is null) return null;
        try
        {
            return device.GetId();
        }
        finally
        {
            Com.Release(device);
        }
    }

    private void RefreshDevices()
    {
        if (_enumerator is null) return;
        var seen = new List<string>();
        foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
        {
            var collection = _enumerator.EnumAudioEndpoints(flow, ComConstants.DeviceStateActive);
            try
            {
                var count = collection.GetCount();
                for (var i = 0; i < count; i++)
                {
                    var device = collection.Item(i);
                    var id = device.GetId();
                    seen.Add(id);
                    if (_devices.ContainsKey(id))
                    {
                        Com.Release(device);
                        continue;
                    }

                    if (CreateDevice(device, id, flow) is { } entry) _devices[id] = entry;
                }
            }
            finally
            {
                Com.Release(collection);
            }
        }

        foreach (var gone in _devices.Keys.Except(seen).ToList())
        {
            DisposeDevice(_devices[gone]);
            _devices.Remove(gone);
        }

        _order = seen.Where(_devices.ContainsKey).ToList();
    }

    private DeviceEntry? CreateDevice(IMMDevice device, string id, EDataFlow flow)
    {
        var entry = new DeviceEntry(id, flow, device);
        try
        {
            ReadDeviceProperties(entry);

            entry.Volume = Com.Activate<IAudioEndpointVolume>(device, ComConstants.IidAudioEndpointVolume);
            entry.Volume.GetMasterVolumeLevelScalar(out var level);
            entry.Volume.GetMute(out var muted);
            entry.Level = level;
            entry.Muted = muted;
            entry.VolumeCallback = new EndpointVolumeCallback(this, id);
            Com.Check(entry.Volume.RegisterControlChangeNotify(entry.VolumeCallback), "RegisterControlChangeNotify");

            entry.Meter = Com.TryActivate<IAudioMeterInformation>(device, ComConstants.IidAudioMeterInformation);

            if (flow == EDataFlow.Render)
            {
                entry.SessionManager = Com.Activate<IAudioSessionManager2>(device, ComConstants.IidAudioSessionManager2);

                // Без першого GetSessionEnumerator служба не надсилає OnSessionCreated; другий перелік після підписки
                // ловить сеанси, що з'явилися між ними (повтори відсіює ідентифікатор екземпляра).
                Com.Release(entry.SessionManager.GetSessionEnumerator());
                entry.SessionCallback = new SessionCreatedCallback(this, id);
                Com.Check(entry.SessionManager.RegisterSessionNotification(entry.SessionCallback), "RegisterSessionNotification");

                var sessions = entry.SessionManager.GetSessionEnumerator();
                try
                {
                    var count = sessions.GetCount();
                    for (var i = 0; i < count; i++) AddSession(entry, sessions.GetSession(i));
                }
                finally
                {
                    Com.Release(sessions);
                }
            }

            return entry;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or AudioCommandException)
        {
            DisposeDevice(entry);
            if (Com.IsServiceGone(ex)) throw;

            // Зниклий за мить пристрій (DEVICE_INVALIDATED) — звичайна справа при під'єднанні USB: наступна подія
            // Windows перелічить пристрої знову.
            _log.Warn($"Пристрій «{entry.Name}» пропущено: {ex.Message}");
            return null;
        }
    }

    private void ReadDeviceProperties(DeviceEntry entry)
    {
        IPropertyStore? store = null;
        try
        {
            store = entry.Device.OpenPropertyStore(ComConstants.StgmRead);
            entry.Name = Com.ReadString(store, PropertyKeys.DeviceFriendlyName)
                         ?? Com.ReadString(store, PropertyKeys.DeviceDescription)
                         ?? Strings.UnknownDevice;
            entry.IconPath = Com.ReadString(store, PropertyKeys.DeviceIconPath);
        }
        catch (COMException)
        {
            entry.Name ??= Strings.UnknownDevice;
        }
        finally
        {
            Com.Release(store);
        }
    }

    private void ReadMicDetails(DeviceEntry device)
    {
        bool? listen = null;
        string? target = null;
        IPropertyStore? store = null;
        try
        {
            store = device.Device.OpenPropertyStore(ComConstants.StgmRead);
            listen = Com.ReadBool(store, PropertyKeys.ListenEnabled);
            target = Com.ReadString(store, PropertyKeys.ListenTarget);
        }
        catch (COMException)
        {
            // Властивостей прослуховування немає — покажемо вимкненим.
        }
        finally
        {
            Com.Release(store);
        }

        device.Details = new MicDetails
        {
            Boost = device.Boost?.Read(),
            ListenSupported = _capabilities.Listen,
            ListenEnabled = listen ?? false,
            ListenTargetId = string.IsNullOrEmpty(target) ? null : target,
        };
    }

    private void DisposeDevice(DeviceEntry entry)
    {
        foreach (var session in entry.Sessions.Values.ToList()) RemoveSession(entry, session);

        if (entry.SessionManager is not null && entry.SessionCallback is not null)
        {
            try { entry.SessionManager.UnregisterSessionNotification(entry.SessionCallback); }
            catch (Exception) { /* служба вже зникла */ }
        }

        if (entry.Volume is not null && entry.VolumeCallback is not null)
        {
            try { entry.Volume.UnregisterControlChangeNotify(entry.VolumeCallback); }
            catch (Exception) { /* служба вже зникла */ }
        }

        entry.Boost?.Dispose();
        Com.Release(entry.SessionManager);
        Com.Release(entry.Meter);
        Com.Release(entry.Volume);
        Com.Release(entry.Device);
        entry.SessionManager = null;
        entry.Meter = null;
        entry.Volume = null;
    }

    // ---- Сеанси ----

    private void AddSession(DeviceEntry device, IAudioSessionControl control)
    {
        if (control is not IAudioSessionControl2 session
            || session.GetState(out var state) < 0
            || state == AudioSessionState.Expired
            || session.GetSessionInstanceIdentifier(out var instance) < 0
            || device.Sessions.ContainsKey(instance))
        {
            Com.Release(control);
            return;
        }

        var entry = new SessionEntry(instance, control, session, control as ISimpleAudioVolume, control as IAudioMeterInformation)
        {
            State = state,
            Sequence = ++_sessionSequence,
            IsSystem = session.IsSystemSoundsSession() == ComConstants.SOk,
        };
        entry.ProcessId = session.GetProcessId(out var processId) >= 0 ? processId : 0;
        if (entry.Simple is not null)
        {
            entry.Simple.GetMasterVolume(out var volume);
            entry.Simple.GetMute(out var muted);
            entry.Volume = volume;
            entry.Muted = muted;
        }

        ReadSessionIdentity(entry);
        entry.Events = new SessionEventsCallback(this, device.Id, instance);
        if (session.RegisterAudioSessionNotification(entry.Events) < 0) entry.Events = null;

        device.Sessions[instance] = entry;
        WatchProcess(entry.ProcessId);
    }

    private void ReadSessionIdentity(SessionEntry entry)
    {
        if (entry.IsSystem)
        {
            entry.AppKey = "system";
            entry.DisplayName = Strings.SystemSounds;
            return;
        }

        var path = ProcessInfo.GetPath(entry.ProcessId);
        entry.ExePath = path;
        entry.AppKey = path?.ToLowerInvariant() ?? $"pid:{entry.ProcessId}";
        entry.ProcessName = ProcessInfo.GetProcessName(path, entry.ProcessId);
        entry.DisplayName = entry.Control2.GetDisplayName(out var name) >= 0 ? ProcessInfo.ResolveDisplayName(name) : null;
        entry.SessionIconPath = entry.Control2.GetIconPath(out var icon) >= 0 && !string.IsNullOrWhiteSpace(icon) && !icon.StartsWith('@')
            ? icon
            : null;
    }

    private void RemoveSession(DeviceEntry device, SessionEntry session)
    {
        if (session.Events is not null)
        {
            try { session.Control2.UnregisterAudioSessionNotification(session.Events); }
            catch (Exception) { /* служба вже зникла */ }
        }

        Com.Release(session.Control);
        device.Sessions.Remove(session.InstanceId);
        UnwatchProcessIfUnused(session.ProcessId);
    }

    private void WatchProcess(int processId)
    {
        if (processId <= 0 || _watchedProcesses.ContainsKey(processId)) return;
        try
        {
            // Сеанс програми, що закрилася, Windows ще довго тримає неактивним; мікшер Windows його ховає — і ми теж.
            var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Post(() => Guarded("процес закрився", () => OnProcessExited(processId)));
            _watchedProcesses[processId] = process;
        }
        catch (Exception)
        {
            // Немає доступу до процесу (наприклад, запущеного від адміністратора) — сеанс зникне сам, коли Windows його закриє.
        }
    }

    private void UnwatchProcessIfUnused(int processId)
    {
        if (processId <= 0 || _devices.Values.Any(d => d.Sessions.Values.Any(s => s.ProcessId == processId))) return;
        if (_watchedProcesses.Remove(processId, out var process)) process.Dispose();
    }

    private void OnProcessExited(int processId)
    {
        foreach (var device in _devices.Values)
        foreach (var session in device.Sessions.Values.Where(s => s.ProcessId == processId).ToList())
            RemoveSession(device, session);
        MarkDirty();
    }

    // ---- Події від Windows (уже в потоці звуку) ----

    private void OnSessionCreated(string deviceId, IAudioSessionControl control)
    {
        if (!_alive || !_devices.TryGetValue(deviceId, out var device))
        {
            Com.Release(control);
            return;
        }

        AddSession(device, control);
        MarkDirty();
    }

    private void OnSessionVolume(string deviceId, string instance, float volume, bool muted)
    {
        if (!TryGetSession(deviceId, instance, out _, out var session)) return;
        session.Volume = volume;
        session.Muted = muted;
        MarkDirty();
    }

    private void OnSessionState(string deviceId, string instance, AudioSessionState state)
    {
        if (!TryGetSession(deviceId, instance, out var device, out var session)) return;
        if (state == AudioSessionState.Expired) RemoveSession(device, session);
        else session.State = state;
        MarkDirty();
    }

    private void OnSessionGone(string deviceId, string instance)
    {
        if (!TryGetSession(deviceId, instance, out var device, out var session)) return;
        RemoveSession(device, session);
        MarkDirty();
    }

    private void OnSessionIdentityChanged(string deviceId, string instance)
    {
        if (!TryGetSession(deviceId, instance, out _, out var session)) return;
        ReadSessionIdentity(session);
        MarkDirty();
    }

    private bool TryGetSession(string deviceId, string instance, out DeviceEntry device, out SessionEntry session)
    {
        session = null!;
        return _devices.TryGetValue(deviceId, out device!) && device.Sessions.TryGetValue(instance, out session!);
    }

    private void OnEndpointVolume(string deviceId, float level, bool muted)
    {
        if (!_devices.TryGetValue(deviceId, out var device)) return;
        device.Level = level;
        device.Muted = muted;
        MarkDirty();
    }

    private void OnDevicePropertyChanged(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var device)) return;
        ReadDeviceProperties(device);
        MarkDirty();
    }

    private void ScheduleDevicesRefresh()
    {
        if (_devicesRefreshScheduled) return;
        _devicesRefreshScheduled = true;

        // Під'єднання навушників дає кілька подій поспіль; перелічуємо пристрої один раз після них.
        Schedule(() => Guarded("пристрої", () =>
        {
            _devicesRefreshScheduled = false;
            RefreshDevices();
            RefreshDefaults();
            ReopenCaptureMeter();
            MarkDirty();
        }), DevicesRefreshDelayMs);
    }

    private void OnDefaultsChanged()
    {
        RefreshDefaults();
        ReopenCaptureMeter();
        MarkDirty();
    }

    // ---- Знімок ----

    private void MarkDirty()
    {
        if (_publishScheduled) return;
        _publishScheduled = true;
        Schedule(() =>
        {
            _publishScheduled = false;
            Publish();
        }, PublishDelayMs);
    }

    private void Publish()
    {
        var snapshot = _alive ? BuildSnapshot() : AudioSnapshot.Empty;
        _current = snapshot;
        try
        {
            Changed?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _log.Error("Обробник змін звуку кинув виняток.", ex);
        }
    }

    private AudioSnapshot BuildSnapshot()
    {
        var devices = new List<AudioDevice>(_order.Count);
        foreach (var id in _order)
        {
            if (!_devices.TryGetValue(id, out var entry)) continue;
            var render = entry.Flow == EDataFlow.Render;
            devices.Add(new AudioDevice
            {
                Id = id,
                Flow = render ? AudioFlow.Render : AudioFlow.Capture,
                Name = entry.Name ?? Strings.UnknownDevice,
                IconPath = entry.IconPath,
                Volume = entry.Level,
                IsMuted = entry.Muted,
                IsDefault = id == (render ? _defaultRender : _defaultCapture),
                IsDefaultCommunications = id == (render ? _defaultRenderCommunications : _defaultCaptureCommunications),
                Apps = render ? BuildApps(entry) : [],
                Details = entry.Details,
            });
        }

        return new AudioSnapshot(devices);
    }

    private List<AudioApp> BuildApps(DeviceEntry device) =>
        device.Sessions.Values
            .GroupBy(s => s.AppKey)
            .Select(group =>
            {
                var sessions = group.OrderBy(s => s.Sequence).ToList();
                var lead = sessions.FirstOrDefault(s => s.State == AudioSessionState.Active) ?? sessions[0];
                return (Order: sessions[0].Sequence, App: new AudioApp
                {
                    Key = group.Key,
                    DeviceId = device.Id,
                    Name = AppName(sessions),
                    IconPath = sessions.Select(s => s.ExePath ?? s.SessionIconPath).FirstOrDefault(p => p is not null),
                    Volume = lead.Volume,
                    IsMuted = sessions.All(s => s.Muted),
                    IsSystemSounds = lead.IsSystem,
                    ProcessName = lead.IsSystem ? null : lead.ProcessName,
                    ProcessIds = sessions.Select(s => s.ProcessId).Where(pid => pid > 0).Distinct().ToArray(),
                    IsActive = sessions.Any(s => s.State == AudioSessionState.Active),
                    RoutedDeviceId = lead.IsSystem ? null : RoutedDevice(group.Key, lead.ProcessId),
                });
            })
            .OrderBy(x => x.App.IsSystemSounds)
            .ThenBy(x => x.Order)
            .Select(x => x.App)
            .ToList();

    private string AppName(List<SessionEntry> sessions)
    {
        if (sessions[0].IsSystem) return Strings.SystemSounds;
        if (sessions.Select(s => s.DisplayName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) is { } display) return display;

        var path = sessions.Select(s => s.ExePath).FirstOrDefault(p => p is not null);
        if (path is not null)
        {
            if (!_descriptions.TryGetValue(path, out var description))
                _descriptions[path] = description = ProcessInfo.GetDescription(path);
            if (description is not null) return description;
        }

        return sessions.Select(s => s.ProcessName).FirstOrDefault(n => n is not null) ?? Strings.UnknownApp;
    }

    private string? RoutedDevice(string appKey, int processId)
    {
        if (_routing is null || processId <= 0) return null;
        if (_routedByApp.TryGetValue(appKey, out var known)) return known;

        var packed = _routing.GetPersisted(processId, EDataFlow.Render, ERole.Multimedia, out var hr);
        var routed = hr >= 0 ? DeviceIds.Unpack(packed) : null;
        _routedByApp[appKey] = routed;
        return routed;
    }

    // ---- Рівні ----

    private void StartMetering()
    {
        if (!_alive || Volatile.Read(ref _meteringCount) == 0) return;
        _meterTimer ??= new Timer(_ =>
        {
            // Нова вибірка лише після того, як попередню вже оброблено: черга не росте, якщо потік зайнятий.
            if (Interlocked.Exchange(ref _meterPending, 1) == 0) Post(SamplePeaks);
        }, null, 0, MeterIntervalMs);
        ReopenCaptureMeter();
    }

    private void StopMetering()
    {
        if (Volatile.Read(ref _meteringCount) > 0) return;
        StopMeteringCore();
    }

    private void StopMeteringCore()
    {
        _meterTimer?.Dispose();
        _meterTimer = null;
        Volatile.Write(ref _meterPending, 0);
        _captureMeter?.Dispose();
        _captureMeter = null;
    }

    /// <summary>
    /// Рівень мікрофона Windows рахує лише тоді, коли його хтось записує. Поки мікшер відкритий, модуль сам слухає
    /// мікрофон за замовчуванням (нічого не читаючи), тож індикатор показує, чи мікрофон чує.
    /// </summary>
    private void ReopenCaptureMeter()
    {
        if (_meterTimer is null) return;
        if (_captureMeter?.DeviceId == _defaultCapture) return;
        _captureMeter?.Dispose();
        _captureMeter = _defaultCapture is not null && _devices.TryGetValue(_defaultCapture, out var mic)
            ? CaptureMeterStream.TryOpen(mic, _log)
            : null;
    }

    private void SamplePeaks()
    {
        Volatile.Write(ref _meterPending, 0);
        if (!_alive || _meterTimer is null) return;

        var values = new Dictionary<string, float>();
        Guarded("рівні", () =>
        {
            foreach (var device in _devices.Values)
            {
                if (device.Meter is not null && device.Meter.GetPeakValue(out var peak) >= 0) values[PeakKeys.Device(device.Id)] = peak;
                foreach (var session in device.Sessions.Values)
                {
                    if (session.Meter is null || session.Meter.GetPeakValue(out var sessionPeak) < 0) continue;
                    var key = PeakKeys.App(device.Id, session.AppKey);
                    values[key] = Math.Max(values.GetValueOrDefault(key), sessionPeak);
                }
            }
        });

        try
        {
            Peaks?.Invoke(this, new PeakSample(values));
        }
        catch (Exception ex)
        {
            _log.Error("Обробник рівнів звуку кинув виняток.", ex);
        }
    }

    // ---- Внутрішні класи ----

    private sealed class DeviceEntry(string id, EDataFlow flow, IMMDevice device)
    {
        public string Id { get; } = id;
        public EDataFlow Flow { get; } = flow;
        public IMMDevice Device { get; } = device;
        public string? Name { get; set; }
        public string? IconPath { get; set; }
        public float Level { get; set; }
        public bool Muted { get; set; }
        public IAudioEndpointVolume? Volume { get; set; }
        public EndpointVolumeCallback? VolumeCallback { get; set; }
        public IAudioMeterInformation? Meter { get; set; }
        public IAudioSessionManager2? SessionManager { get; set; }
        public SessionCreatedCallback? SessionCallback { get; set; }
        public Dictionary<string, SessionEntry> Sessions { get; } = new(StringComparer.Ordinal);
        public bool BoostSearched { get; set; }
        public MicBoostControl? Boost { get; set; }
        public MicDetails? Details { get; set; }
    }

    private sealed class SessionEntry(
        string instanceId,
        IAudioSessionControl control,
        IAudioSessionControl2 control2,
        ISimpleAudioVolume? simple,
        IAudioMeterInformation? meter)
    {
        public string InstanceId { get; } = instanceId;
        public IAudioSessionControl Control { get; } = control;
        public IAudioSessionControl2 Control2 { get; } = control2;
        public ISimpleAudioVolume? Simple { get; } = simple;
        public IAudioMeterInformation? Meter { get; } = meter;
        public SessionEventsCallback? Events { get; set; }
        public long Sequence { get; init; }
        public bool IsSystem { get; init; }
        public int ProcessId { get; set; }
        public AudioSessionState State { get; set; }
        public float Volume { get; set; }
        public bool Muted { get; set; }
        public string AppKey { get; set; } = "";
        public string? ExePath { get; set; }
        public string? ProcessName { get; set; }
        public string? DisplayName { get; set; }
        public string? SessionIconPath { get; set; }
    }

    private sealed class MeteringTicket(CoreAudioBackend owner, bool microphone) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.EndMetering(microphone);
        }
    }

    // Зворотні виклики Windows приходять у її потоках: лише ставимо роботу в чергу й одразу повертаємо S_OK.

    [ComVisible(true)]
    private sealed class NotificationClient(CoreAudioBackend owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState) => Queue(owner.ScheduleDevicesRefresh);

        public int OnDeviceAdded(string deviceId) => Queue(owner.ScheduleDevicesRefresh);

        public int OnDeviceRemoved(string deviceId) => Queue(owner.ScheduleDevicesRefresh);

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
            Queue(() => owner.Guarded("пристрій за замовчуванням", owner.OnDefaultsChanged));

        public int OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            if (key.Is(PropertyKeys.DeviceFriendlyName) || key.Is(PropertyKeys.DeviceIconPath))
                Queue(() => owner.Guarded("назва пристрою", () => owner.OnDevicePropertyChanged(deviceId)));
            return ComConstants.SOk;
        }

        private int Queue(Action action)
        {
            owner.Post(action);
            return ComConstants.SOk;
        }
    }

    [ComVisible(true)]
    private sealed class EndpointVolumeCallback(CoreAudioBackend owner, string deviceId) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            if (notifyData == IntPtr.Zero) return ComConstants.SOk;

            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext (16), BOOL bMuted (4), float fMasterVolume (4).
            // Дані живі лише під час виклику, тож читаємо їх тут.
            var muted = Marshal.ReadInt32(notifyData, 16) != 0;
            var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(notifyData, 20));
            owner.Post(() => owner.Guarded("гучність пристрою", () => owner.OnEndpointVolume(deviceId, level, muted)));
            return ComConstants.SOk;
        }
    }

    [ComVisible(true)]
    private sealed class SessionCreatedCallback(CoreAudioBackend owner, string deviceId) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            owner.Post(() => owner.Guarded("новий сеанс", () => owner.OnSessionCreated(deviceId, newSession)));
            return ComConstants.SOk;
        }
    }

    [ComVisible(true)]
    private sealed class SessionEventsCallback(CoreAudioBackend owner, string deviceId, string instance) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string newDisplayName, ref Guid eventContext) => Identity();

        public int OnIconPathChanged(string newIconPath, ref Guid eventContext) => Identity();

        public int OnSimpleVolumeChanged(float newVolume, bool newMute, ref Guid eventContext)
        {
            owner.Post(() => owner.Guarded("гучність програми", () => owner.OnSessionVolume(deviceId, instance, newVolume, newMute)));
            return ComConstants.SOk;
        }

        public int OnChannelVolumeChanged(int channelCount, IntPtr newChannelVolumes, int changedChannel, ref Guid eventContext) =>
            ComConstants.SOk;

        public int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext) => ComConstants.SOk;

        public int OnStateChanged(AudioSessionState newState)
        {
            owner.Post(() => owner.Guarded("стан сеансу", () => owner.OnSessionState(deviceId, instance, newState)));
            return ComConstants.SOk;
        }

        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            owner.Post(() => owner.Guarded("сеанс відключено", () => owner.OnSessionGone(deviceId, instance)));
            return ComConstants.SOk;
        }

        private int Identity()
        {
            owner.Post(() => owner.Guarded("назва програми", () => owner.OnSessionIdentityChanged(deviceId, instance)));
            return ComConstants.SOk;
        }
    }

    /// <summary>Потік запису без читання: лише щоб Windows рахувала рівень мікрофона, поки мікшер відкритий.</summary>
    private sealed class CaptureMeterStream : IDisposable
    {
        private const long BufferDuration100Ns = 10_000_000;

        private IAudioClient? _client;

        private CaptureMeterStream(string deviceId, IAudioClient client)
        {
            DeviceId = deviceId;
            _client = client;
        }

        public string DeviceId { get; }

        public static CaptureMeterStream? TryOpen(DeviceEntry device, IModuleLogger log)
        {
            var client = Com.TryActivate<IAudioClient>(device.Device, ComConstants.IidAudioClient);
            if (client is null) return null;

            var hr = client.GetMixFormat(out var format);
            if (hr >= 0)
            {
                hr = client.Initialize(ComConstants.AudclntShareModeShared, 0, BufferDuration100Ns, 0, format, IntPtr.Zero);
                NativeMethods.CoTaskMemFree(format);
            }

            if (hr >= 0) hr = client.Start();
            if (hr >= 0) return new CaptureMeterStream(device.Id, client);

            // E_ACCESSDENIED — у Параметрах заборонено доступ настільних програм до мікрофона.
            log.Info($"Рівень мікрофона «{device.Name}» недоступний (0x{hr:X8}).");
            Com.Release(client);
            return null;
        }

        public void Dispose()
        {
            if (_client is null) return;
            try { _client.Stop(); }
            catch (Exception) { /* служба вже зникла */ }
            Com.Release(_client);
            _client = null;
        }
    }
}
