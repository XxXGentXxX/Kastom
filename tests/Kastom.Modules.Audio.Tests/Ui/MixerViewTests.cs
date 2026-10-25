using System.Windows;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

public sealed class MixerStringsTests
{
    [Fact]
    public void EveryStringPropertyHasResxEntry() =>
        ModuleDefinitionTests.AssertResxMatches(typeof(MixerStrings), "MixerStrings.resx");
}

public sealed class MixerLayoutTests
{
    [Fact]
    public void PageShowsCardForEveryDeviceAndRowForEveryApp()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();

            Assert.Equal([DemoIds.Headphones, DemoIds.Speakers, DemoIds.Monitor], f.View.OutputCards.Select(c => c.Id));
            Assert.Equal([DemoIds.HeadsetMic, DemoIds.WebcamMic], f.View.InputCards.Select(c => c.Id));
            Assert.Equal(4, f.Output(DemoIds.Headphones).Apps.Count);
            Assert.Single(f.Output(DemoIds.Speakers).Apps);
            Assert.Empty(f.Output(DemoIds.Monitor).Apps);
            Assert.False(f.View.IsUnavailableShown);
        });
    }

    [Fact]
    public void SystemSoundsIsLastRowWithDividerEvenWhenSnapshotPutsItFirst()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var snapshot = f.Backend.Current;
            f.Backend.Replace(snapshot with
            {
                Devices = snapshot.Devices
                    .Select(d => d.Id != DemoIds.Headphones ? d : d with { Apps = d.Apps.OrderByDescending(a => a.IsSystemSounds).ToArray() })
                    .ToArray(),
            });
            Sta.DoEvents();

            var apps = f.Output(DemoIds.Headphones).Apps;
            Assert.True(apps[^1].App.IsSystemSounds);
            Assert.True(apps[^1].IsDividerVisible);
            Assert.All(apps.Take(3), a => Assert.False(a.IsDividerVisible));
            Assert.Equal(["chrome", "discord", "cs2"], apps.Take(3).Select(a => a.App.ProcessName));
        });
    }

    [Fact]
    public void DefaultDeviceGoesFirstAndCardsAreKept()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var headphones = f.Output(DemoIds.Headphones);
            var speakers = f.Output(DemoIds.Speakers);

            f.Backend.SetDefaultDeviceAsync(DemoIds.Speakers, DefaultRoles.All).GetAwaiter().GetResult();
            Sta.DoEvents();

            Assert.Equal([DemoIds.Speakers, DemoIds.Headphones, DemoIds.Monitor], f.View.OutputCards.Select(c => c.Id));
            Assert.Same(headphones, f.Output(DemoIds.Headphones));
            Assert.Same(speakers, f.Output(DemoIds.Speakers));
            Assert.True(speakers.IsDefaultBadgeVisible);
            Assert.False(headphones.IsDefaultBadgeVisible);
        });
    }

    [Fact]
    public void RemovedAppsDisappearAndNewOnesAppear()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var discord = f.App(DemoIds.Headphones, @"c:\users\user\appdata\local\discord\app-1.0.9200\discord.exe");
            var snapshot = f.Backend.Current;
            var extra = snapshot.FindDevice(DemoIds.Headphones)!.Apps[0] with { Key = "pid:1", Name = "Нова програма", IconPath = null, ProcessName = "newapp" };

            f.Backend.Replace(snapshot with
            {
                Devices = snapshot.Devices
                    .Select(d => d.Id != DemoIds.Headphones ? d : d with { Apps = d.Apps.Where(a => a.ProcessName != "chrome").Append(extra).ToArray() })
                    .ToArray(),
            });
            Sta.DoEvents();

            var apps = f.Output(DemoIds.Headphones).Apps;
            Assert.DoesNotContain(apps, a => a.App.ProcessName == "chrome");
            Assert.Contains(apps, a => a.App.Key == "pid:1");
            Assert.Same(discord, apps.First(a => a.App.ProcessName == "discord"));
            Assert.True(apps[^1].App.IsSystemSounds);
        });
    }

    [Fact]
    public void WidgetShowsOnlyDefaultsWithoutApps()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(MixerMode.Widget);

            Assert.Empty(f.View.OutputCards);
            Assert.Empty(f.View.InputCards);
            Assert.Empty(f.View.Descendants<AppRow>());
            Assert.Empty(f.View.Descendants<MoreButton>());
            Assert.Equal("Навушники (HyperX Cloud II)", f.View.WidgetOutputName);
            Assert.Equal("Мікрофон (HyperX Cloud II)", f.View.WidgetMicName);
            Assert.Equal(45, f.View.WidgetVolume!.Slider.Value);
        });
    }

    [Fact]
    public void WidgetSendsVolumeAndMicMute()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(MixerMode.Widget);

            f.View.WidgetVolume!.Slider.Value = 30;
            f.View.WidgetMicButton.Press();

            Assert.Contains($"SetDeviceVolume {DemoIds.Headphones} {0.3f:0.###}", f.Backend.Commands);
            Assert.Contains($"SetDeviceMute {DemoIds.HeadsetMic} True", f.Backend.Commands);
        });
    }

    [Fact]
    public void UnavailableSnapshotShowsOneMessage()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(start: false);

            Assert.True(f.View.IsUnavailableShown);
            Assert.Equal("Звукова служба Windows недоступна", f.View.UnavailableText);
            Assert.Empty(f.View.OutputCards);
            Assert.Empty(f.View.InputCards);

            f.Backend.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Sta.DoEvents();

            Assert.False(f.View.IsUnavailableShown);
            Assert.Equal(3, f.View.OutputCards.Count);
        });
    }

    [Fact]
    public void WidgetWithoutServiceShowsMessage()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(MixerMode.Widget, start: false);
            Assert.True(f.View.IsUnavailableShown);
        });
    }

    [Fact]
    public void DeviceGlyphDependsOnName()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();

            Assert.Equal(Glyphs.Headphones, f.Output(DemoIds.Headphones).GlyphText);
            Assert.Equal(Glyphs.Speaker, f.Output(DemoIds.Speakers).GlyphText);
            Assert.Equal(Glyphs.Microphone, f.Input(DemoIds.HeadsetMic).GlyphText);
            Assert.Equal(Glyphs.Headphones, Glyphs.ForOutput("USB Headset"));
            Assert.Equal(Glyphs.Headphones, Glyphs.ForOutput("HEADPHONES"));
        });
    }

    [Theory]
    [InlineData(0, "\uE992")]
    [InlineData(1, "\uE993")]
    [InlineData(32, "\uE993")]
    [InlineData(33, "\uE994")]
    [InlineData(65, "\uE994")]
    [InlineData(66, "\uE995")]
    [InlineData(100, "\uE995")]
    public void VolumeGlyphFollowsLevel(int percent, string expected) => Assert.Equal(expected, Glyphs.Volume(percent));

    [Fact]
    public void MutedStateShowsGlyphAndDimsAppRow()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var cs2 = f.Output(DemoIds.Headphones).Apps.First(a => a.App.ProcessName == "cs2");
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            Assert.Equal(Glyphs.Muted, cs2.Row.Mute.GlyphText);
            Assert.Equal(0.55, cs2.BodyOpacity);
            Assert.Equal(1, chrome.BodyOpacity);
            Assert.Equal(Glyphs.Volume(80), chrome.Row.Mute.GlyphText);

            var monitor = f.Output(DemoIds.Monitor);
            Assert.Equal(Glyphs.Muted, monitor.Row.Mute.GlyphText);

            var webcam = f.Input(DemoIds.WebcamMic);
            Assert.Equal(Glyphs.Microphone, webcam.Row.Mute.GlyphText);
            Assert.True(webcam.Row.Mute.StrikeVisible);
            Assert.False(f.Input(DemoIds.HeadsetMic).Row.Mute.StrikeVisible);
        });
    }

    /// <summary>Посилання унизу сторінки; посилання всередині налаштувань мікрофона до них не належать.</summary>
    private static List<LinkButton> PageLinks(MixerView view) =>
        view.Descendants<LinkButton>().Where(l => !l.IsDescendantOfMicSettings()).ToList();

    [Fact]
    public void PageHasLinksToWindowsSettings()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var links = PageLinks(f.View);

            Assert.Equal(3, links.Count);
            foreach (var link in links) link.Press();
            Assert.Equal(["OpenSoundSettings", "OpenAppVolumeSettings", "OpenTaskbarSettings"], f.Shell.Calls);
        });
    }

    [Fact]
    public void FlyoutHasTitleAndGearButWidgetHasNoLinks()
    {
        Sta.Run(() =>
        {
            var flyout = MixerFixture.Create(MixerMode.Flyout);
            var gear = flyout.View.Descendants<SubtleButton>().First(b => b.Content is System.Windows.Controls.TextBlock { Text: Glyphs.Settings });
            gear.Press();
            Assert.Equal(["OpenSoundSettings"], flyout.Shell.Calls);
            Assert.Empty(PageLinks(flyout.View));

            var widget = MixerFixture.Create(MixerMode.Widget);
            Assert.Empty(PageLinks(widget.View));
        });
    }

    [Fact]
    public void AutomationNamesNameTheDevice()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            Assert.Equal("Гучність: Google Chrome", System.Windows.Automation.AutomationProperties.GetName(chrome.Row.Slider));
            Assert.Equal("Вимкнути звук: Google Chrome", System.Windows.Automation.AutomationProperties.GetName(chrome.Row.Mute));
            Assert.Equal("Ще: Google Chrome", System.Windows.Automation.AutomationProperties.GetName(chrome.More));

            var cs2 = f.Output(DemoIds.Headphones).Apps.First(a => a.App.ProcessName == "cs2");
            Assert.Equal("Увімкнути звук: Counter-Strike 2", System.Windows.Automation.AutomationProperties.GetName(cs2.Row.Mute));
            Assert.Equal("Зробити основним: Динаміки (Realtek(R) Audio)",
                System.Windows.Automation.AutomationProperties.GetName(f.Output(DemoIds.Speakers).MakeDefaultButton));
        });
    }
}
