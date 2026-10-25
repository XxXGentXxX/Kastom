using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Плитки «Звук» і «Мікрофони» внизу панелі біля годинника (прохання власника 2026-10-25).</summary>
public sealed class QuickTilesTests
{
    private static readonly string[] OutputIds = [DemoIds.Headphones, DemoIds.Speakers, DemoIds.Monitor];
    private static readonly string[] MicIds = [DemoIds.HeadsetMic, DemoIds.WebcamMic];

    private static AudioSnapshot WithMuted(AudioSnapshot snapshot, Func<AudioDevice, bool> muted) =>
        snapshot with { Devices = snapshot.Devices.Select(d => d with { IsMuted = muted(d) }).ToArray() };

    private static IEnumerable<string> MuteCommands(MixerFixture f) =>
        f.Backend.Commands.Where(c => c.StartsWith("SetDeviceMute", StringComparison.Ordinal));

    [Fact]
    public void OnlyThePanelHasTiles() => Sta.Run(() =>
    {
        Assert.NotNull(MixerFixture.Create(MixerMode.Flyout).View.Tiles);
        Assert.Null(MixerFixture.Create(MixerMode.Page).View.Tiles);
        Assert.Null(MixerFixture.Create(MixerMode.Widget).View.Tiles);
    });

    [Fact]
    public void PanelPutsTilesAtTheBottomBelowTheScroll() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout, attach: false);
        var flyout = new MixerFlyout(f.Host);

        var tiles = flyout.View.Tiles!;
        var dock = Assert.IsType<DockPanel>(tiles.Parent);
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(tiles));
        Assert.IsType<ScrollViewer>(dock.Children[^1]);
        Assert.Same(dock, ((Border)flyout.Content).Child);

        // Плитки стоять поряд: ліва — звук, права — мікрофони.
        Assert.Equal(0, Grid.GetColumn(tiles.Sound));
        Assert.Equal(2, Grid.GetColumn(tiles.Mics));
    });

    [Fact]
    public void TilesAreShownInCompactMode() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout, configure: s => s.Set(SettingKeys.Compact, true));

        Assert.Equal(Visibility.Visible, f.View.Tiles!.Visibility);
        Assert.Equal(Visibility.Visible, f.View.Tiles.Sound.Visibility);
        Assert.Equal(Visibility.Visible, f.View.Tiles.Mics.Visibility);
        Assert.All(f.View.OutputCards, c => Assert.False(c.AreAppsShown));
    });

    [Fact]
    public void TilesAreHiddenWhenAudioIsUnavailable() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout, start: false);
        f.View.RefreshNow();

        Assert.Equal(Visibility.Collapsed, f.View.Tiles!.Visibility);
    });

    [Fact]
    public void MixedStateCountsAsOn() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        var tiles = f.View.Tiles!;

        // У демо-знімку монітор і веб-камера вимкнені, решта — ні.
        Assert.True(tiles.Sound.IsOn);
        Assert.True(tiles.Mics.IsOn);
        Assert.True(tiles.Sound.Toggle.IsChecked);
        Assert.Equal("Увімкнено", tiles.Sound.ToggleText);
        Assert.Equal("Увімкнено", tiles.Mics.ToggleText);
        Assert.False(tiles.Sound.IsGlyphCritical);
        Assert.False(tiles.Mics.IsGlyphCritical);
    });

    [Fact]
    public void GlyphsAreSpeakerAndMicrophone() => Sta.Run(() =>
    {
        var tiles = MixerFixture.Create(MixerMode.Flyout).View.Tiles!;

        Assert.Equal(Glyphs.Speaker, tiles.Sound.GlyphText);
        Assert.Equal(Glyphs.Microphone, tiles.Mics.GlyphText);
    });

    [Fact]
    public void ClickWhenOnMutesEveryDeviceOfThatKind() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);

        f.View.Tiles!.Sound.Button.Press();
        Sta.DoEvents();

        Assert.Equal(OutputIds.Select(id => $"SetDeviceMute {id} True"), MuteCommands(f));
        Assert.All(f.Backend.Current.Outputs, d => Assert.True(d.IsMuted));
        Assert.All(f.Backend.Current.Inputs, d => Assert.Equal(d.Id == DemoIds.WebcamMic, d.IsMuted));

        var sound = f.View.Tiles.Sound;
        Assert.False(sound.IsOn);
        Assert.Equal("Вимкнено", sound.ToggleText);
        Assert.False(sound.Toggle.IsChecked);
        Assert.True(sound.IsGlyphCritical);
        Assert.False(f.View.Tiles.Mics.IsGlyphCritical);
    });

    [Fact]
    public void ClickWhenAllAreMutedUnmutesEveryDevice() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        f.Backend.Replace(WithMuted(f.Backend.Current, _ => true));
        Sta.DoEvents();
        Assert.False(f.View.Tiles!.Mics.IsOn);
        f.Backend.Commands.Clear();

        f.View.Tiles.Mics.Button.Press();
        Sta.DoEvents();

        Assert.Equal(MicIds.Select(id => $"SetDeviceMute {id} False"), MuteCommands(f));
        Assert.All(f.Backend.Current.Inputs, d => Assert.False(d.IsMuted));
        // Звук не зачеплено.
        Assert.All(f.Backend.Current.Outputs, d => Assert.True(d.IsMuted));
        Assert.True(f.View.Tiles.Mics.IsOn);
        Assert.Equal("Увімкнено", f.View.Tiles.Mics.ToggleText);
        Assert.False(f.View.Tiles.Mics.IsGlyphCritical);
        Assert.True(f.View.Tiles.Sound.IsGlyphCritical);
    });

    [Fact]
    public void ToggleDoesTheSameAsTheButton() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);

        f.View.Tiles!.Mics.Toggle.Press();
        Sta.DoEvents();
        Assert.Equal(MicIds.Select(id => $"SetDeviceMute {id} True"), MuteCommands(f));
        Assert.False(f.View.Tiles.Mics.IsOn);

        f.Backend.Commands.Clear();
        f.View.Tiles.Mics.Toggle.Press();
        Sta.DoEvents();
        Assert.Equal(MicIds.Select(id => $"SetDeviceMute {id} False"), MuteCommands(f));
        Assert.True(f.View.Tiles.Mics.IsOn);
    });

    [Fact]
    public void ClickDecidesByTheFreshSnapshotNotByWhatIsDrawn() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        f.Backend.Replace(WithMuted(f.Backend.Current, d => d.Flow == AudioFlow.Render));
        // Перемальовування ще в черзі: плитка досі показує «увімкнено».
        Assert.True(f.View.Tiles!.Sound.IsOn);

        f.View.Tiles.Sound.Button.Press();

        Assert.Equal(OutputIds.Select(id => $"SetDeviceMute {id} False"), MuteCommands(f));
    });

    [Fact]
    public void FailureOnOneDeviceIsReportedAndTheRestAreStillSet() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        f.Backend.FailNext = new AudioCommandException("відмова");

        f.View.Tiles!.Sound.Button.Press();
        Sta.DoEvents();

        Assert.Equal(Strings.OsdCommandFailed, Assert.Single(f.Shell.Errors).Message);
        Assert.Equal(OutputIds.Length, MuteCommands(f).Count());
        Assert.True(f.Backend.Current.FindDevice(DemoIds.Speakers)!.IsMuted);
    });

    [Fact]
    public void TilesAreDisabledWithoutDevicesOfTheKind() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        f.Backend.Replace(new AudioSnapshot(f.Backend.Current.Outputs.ToArray()));
        Sta.DoEvents();

        var tiles = f.View.Tiles!;
        Assert.True(tiles.Sound.Button.IsEnabled);
        Assert.False(tiles.Mics.Button.IsEnabled);
        Assert.False(tiles.Mics.Toggle.IsEnabled);
        Assert.False(tiles.Mics.IsGlyphCritical);

        f.Backend.Commands.Clear();
        tiles.Mics.Button.Press();
        Assert.Empty(f.Backend.Commands);
    });

    [Fact]
    public void ButtonsAndTogglesHaveAccessibleNames() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        var tiles = f.View.Tiles!;

        Assert.Equal("Вимкнути звук усіх пристроїв", AutomationProperties.GetName(tiles.Sound.Button));
        Assert.Equal("Вимкнути всі мікрофони", AutomationProperties.GetName(tiles.Mics.Button));
        Assert.Equal("Звук усіх пристроїв", AutomationProperties.GetName(tiles.Sound.Toggle));
        Assert.Equal("Усі мікрофони", AutomationProperties.GetName(tiles.Mics.Toggle));

        f.Backend.Replace(WithMuted(f.Backend.Current, _ => true));
        Sta.DoEvents();
        Assert.Equal("Увімкнути звук усіх пристроїв", AutomationProperties.GetName(tiles.Sound.Button));
        Assert.Equal("Увімкнути всі мікрофони", AutomationProperties.GetName(tiles.Mics.Button));
    });

    [Fact]
    public void TileTitlesAreSoundAndMics() => Sta.Run(() =>
    {
        var tiles = MixerFixture.Create(MixerMode.Flyout).View.Tiles!;

        Assert.Contains(tiles.Sound.Descendants<TextBlock>(), t => t.Text == "Звук");
        Assert.Contains(tiles.Mics.Descendants<TextBlock>(), t => t.Text == "Мікрофони");
    });
}
