using Kastom.Modules.Audio.Model;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>Звук Windows через Core Audio. ЗАГЛУШКА: справжню реалізацію пише елемент E1.</summary>
public sealed class CoreAudioBackend(IModuleLogger log) : IAudioBackend
{
    private readonly IModuleLogger _log = log;

    public AudioSnapshot Current => AudioSnapshot.Empty;

    public BackendCapabilities Capabilities => BackendCapabilities.None;

    public event EventHandler<AudioSnapshot>? Changed { add { } remove { } }

    public event EventHandler<PeakSample>? Peaks { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _log.Warn("Core Audio ще не реалізовано.");
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;

    public void Dispose()
    {
    }

    public Task SetDeviceVolumeAsync(string deviceId, float volume) => NotReady();

    public Task SetDeviceMuteAsync(string deviceId, bool muted) => NotReady();

    public Task SetAppVolumeAsync(string deviceId, string appKey, float volume) => NotReady();

    public Task SetAppMuteAsync(string deviceId, string appKey, bool muted) => NotReady();

    public Task SetDefaultDeviceAsync(string deviceId, DefaultRoles roles) => NotReady();

    public Task SetAppOutputAsync(string deviceId, string appKey, string? targetDeviceId) => NotReady();

    public Task ResetAppOutputsAsync() => NotReady();

    public Task RequestMicDetailsAsync(string captureDeviceId) => NotReady();

    public Task SetListenAsync(string captureDeviceId, bool enabled, string? targetDeviceId) => NotReady();

    public Task SetBoostAsync(string captureDeviceId, float levelDb) => NotReady();

    public IDisposable BeginMetering() => new Nothing();

    private static Task NotReady() => Task.FromException(new AudioCommandException("Core Audio ще не реалізовано."));

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
