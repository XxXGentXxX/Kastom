using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Під неосновним пристроєм — лише програми, що звучать або направлені туди (так виглядав справжній ПК з трьома виходами).</summary>
public sealed class AppVisibilityTests
{
    private const string Device = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000000}";
    private const string OtherDevice = "{0.0.0.00000000}.{bbbbbbbb-0000-0000-0000-000000000000}";

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
            App("teams", routed: OtherDevice),
        ],
    };

    [Fact]
    public void DefaultDeviceShowsEverythingExceptSilentSessionsRoutedElsewhere() =>
        Assert.Equal(["discord", "chrome", "spotify", "system"], DeviceCard.VisibleApps(Output(isDefault: true)).Select(a => a.Key));

    [Fact]
    public void OtherDeviceShowsOnlyPlayingOrRouted() =>
        Assert.Equal(["chrome", "spotify"], DeviceCard.VisibleApps(Output(isDefault: false)).Select(a => a.Key));

    [Fact]
    public void PlayingSessionIsShownEvenIfTheAppIsRoutedElsewhere()
    {
        var device = Output(isDefault: true) with { Apps = [App("teams", active: true, routed: OtherDevice)] };

        Assert.Equal(["teams"], DeviceCard.VisibleApps(device).Select(a => a.Key));
    }

    [Theory]
    [InlineData(true, new[] { "discord", "chrome", "spotify", "system", "teams" })]
    [InlineData(false, new[] { "chrome", "spotify", "teams" })]
    public void BusyRowIsKeptEvenWhenTheAppFellSilent(bool isDefault, string[] expected)
    {
        var visible = DeviceCard.VisibleApps(Output(isDefault), a => a.Key is "teams");

        Assert.Equal(expected, visible.Select(a => a.Key));
    }

    [Fact]
    public void RowBeingDraggedSurvivesTheAppFallingSilentAndGoesAfterwards() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
        Assert.Contains(f.Output(DemoIds.Headphones).Apps, r => r.App.Key == DemoIds.ChromeKey);

        // Chrome замовк і Windows направила його на колонки: під навушниками його сеанс уже зайвий.
        AudioSnapshot Silent() => f.WithApp(DemoIds.Headphones, DemoIds.ChromeKey, a => a with { IsActive = false, RoutedDeviceId = DemoIds.Speakers });

        chrome.Row.Slider.DragStarted();
        f.Backend.Replace(Silent());
        Sta.DoEvents();
        Assert.Contains(f.Output(DemoIds.Headphones).Apps, r => r.App.Key == DemoIds.ChromeKey);

        chrome.Row.Slider.DragCompleted();
        f.Backend.Replace(Silent());
        Sta.DoEvents();
        Assert.DoesNotContain(f.Output(DemoIds.Headphones).Apps, r => r.App.Key == DemoIds.ChromeKey);
    });
}
