using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Під неосновним пристроєм — лише програми, що звучать або направлені туди (так виглядав справжній ПК з трьома виходами).</summary>
public sealed class AppVisibilityTests
{
    private const string Device = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000000}";

    private static AudioApp App(string key, bool active = false, bool system = false, string? routed = null) => new()
    {
        Key = key,
        DeviceId = Device,
        Name = key,
        Volume = 1,
        IsMuted = false,
        IsActive = active,
        IsSystemSounds = system,
        RoutedDeviceId = routed,
    };

    private static AudioDevice Output(bool isDefault) => new()
    {
        Id = Device,
        Flow = AudioFlow.Render,
        Name = "Speakers",
        Volume = 0.4f,
        IsMuted = false,
        IsDefault = isDefault,
        Apps =
        [
            App("discord"),
            App("chrome", active: true),
            App("spotify", routed: Device),
            App("system", active: true, system: true),
        ],
    };

    [Fact]
    public void DefaultDeviceShowsEverything() =>
        Assert.Equal(["discord", "chrome", "spotify", "system"], DeviceCard.VisibleApps(Output(isDefault: true)).Select(a => a.Key));

    [Fact]
    public void OtherDeviceShowsOnlyPlayingOrRouted() =>
        Assert.Equal(["chrome", "spotify"], DeviceCard.VisibleApps(Output(isDefault: false)).Select(a => a.Key));
}
