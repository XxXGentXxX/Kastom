using Kastom.Modules.Audio.Fake;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.Tests;

/// <summary>Фальшивий бекенд поводиться так, як обіцяє IAudioBackend: на ньому стоять тести мікшера й дій.</summary>
public sealed class FakeBackendTests
{
    private static async Task<FakeAudioBackend> Started()
    {
        var backend = FakeAudioBackend.CreateDemo();
        await backend.StartAsync(CancellationToken.None);
        return backend;
    }

    [Fact]
    public async Task EmptyUntilStartedAndAfterStop()
    {
        var backend = FakeAudioBackend.CreateDemo();
        Assert.False(backend.Current.IsAvailable);
        await backend.StartAsync(CancellationToken.None);
        Assert.True(backend.Current.IsAvailable);
        Assert.Equal(3, backend.Current.Outputs.Count());
        await backend.StopAsync();
        Assert.Empty(backend.Current.Devices);
    }

    [Fact]
    public async Task DefaultSwitchHonoursRoles()
    {
        var backend = await Started();
        var speakers = backend.Current.Outputs.First(d => !d.IsDefault);

        await backend.SetDefaultDeviceAsync(speakers.Id, DefaultRoles.Normal);
        Assert.Equal(speakers.Id, backend.Current.DefaultOutput!.Id);
        Assert.False(backend.Current.FindDevice(speakers.Id)!.IsDefaultCommunications);

        await backend.SetDefaultDeviceAsync(speakers.Id, DefaultRoles.Communications);
        Assert.True(backend.Current.FindDevice(speakers.Id)!.IsDefaultCommunications);
        Assert.Single(backend.Current.Outputs, d => d.IsDefaultCommunications);
        Assert.Single(backend.Current.Inputs, d => d.IsDefault);
    }

    [Fact]
    public async Task AppOutputMovesAppAndResetBringsItBack()
    {
        var backend = await Started();
        var headphones = backend.Current.DefaultOutput!;
        var discord = headphones.Apps.First(a => a.ProcessName == "discord");
        var monitor = backend.Current.Outputs.Last();

        await backend.SetAppOutputAsync(headphones.Id, discord.Key, monitor.Id);
        Assert.DoesNotContain(backend.Current.FindDevice(headphones.Id)!.Apps, a => a.Key == discord.Key);
        Assert.Equal(monitor.Id, backend.Current.FindDevice(monitor.Id)!.Apps.Single(a => a.Key == discord.Key).RoutedDeviceId);

        await backend.ResetAppOutputsAsync();
        Assert.Contains(backend.Current.FindDevice(headphones.Id)!.Apps, a => a.Key == discord.Key && a.RoutedDeviceId is null);
        Assert.DoesNotContain(backend.Current.AllApps, a => a.RoutedDeviceId is not null);
    }

    [Fact]
    public async Task CommandsRaiseChangedAndFailuresDoNot()
    {
        var backend = await Started();
        var changes = 0;
        backend.Changed += (_, _) => changes++;
        var device = backend.Current.DefaultOutput!;

        await backend.SetDeviceVolumeAsync(device.Id, 1.5f);
        Assert.Equal(1f, backend.Current.DefaultOutput!.Volume);
        Assert.Equal(1, changes);

        backend.FailNext = new AudioCommandException("тест");
        await Assert.ThrowsAsync<AudioCommandException>(() => backend.SetDeviceMuteAsync(device.Id, true));
        Assert.False(backend.Current.DefaultOutput!.IsMuted);
        Assert.Equal(1, changes);

        await Assert.ThrowsAsync<AudioCommandException>(() => backend.SetAppMuteAsync(device.Id, "немає", true));
    }

    [Fact]
    public async Task MicDetailsBoostAndListen()
    {
        var backend = await Started();
        var mic = backend.Current.DefaultInput!;
        Assert.Null(mic.Details);

        await backend.RequestMicDetailsAsync(mic.Id);
        Assert.Equal(10, backend.Current.DefaultInput!.Details!.Boost!.LevelDb);

        await backend.SetBoostAsync(mic.Id, 17);
        Assert.Equal(20, backend.Current.DefaultInput!.Details!.Boost!.LevelDb);
        await backend.SetBoostAsync(mic.Id, 99);
        Assert.Equal(30, backend.Current.DefaultInput!.Details!.Boost!.LevelDb);

        await backend.SetListenAsync(mic.Id, true, null);
        Assert.True(backend.Current.DefaultInput!.Details!.ListenEnabled);
    }

    [Fact]
    public async Task MeteringTicketsAreCounted()
    {
        var backend = await Started();
        PeakSample? received = null;
        backend.Peaks += (_, sample) => received = sample;

        var first = backend.BeginMetering();
        var second = backend.BeginMetering();
        Assert.NotNull(received);
        Assert.Equal(2, backend.MeteringCount);
        first.Dispose();
        first.Dispose();
        Assert.Equal(1, backend.MeteringCount);
        second.Dispose();
        Assert.Equal(0, backend.MeteringCount);
    }
}
