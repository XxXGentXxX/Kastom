using System.Windows;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

public sealed class MixerMicTests
{
    [Fact]
    public void ExpandRequestsDetailsOnceAndShowsLoadingUntilTheyArrive()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            Assert.NotNull(mic.MicSettings);
            Assert.Equal(Visibility.Collapsed, mic.MicSettings!.Visibility);

            mic.ExpandButton!.Press();

            Assert.Equal(Visibility.Visible, mic.MicSettings.Visibility);
            Assert.Equal($"RequestMicDetails {DemoIds.HeadsetMic}", f.Backend.Commands[^1]);
            Assert.True(mic.MicSettings.IsLoadingVisible);
            Assert.False(mic.MicSettings.IsBoostVisible);

            Sta.DoEvents();
            Assert.False(mic.MicSettings.IsLoadingVisible);

            mic.ExpandButton!.Press();
            mic.ExpandButton!.Press();
            Assert.Equal(1, f.Backend.Commands.Count(c => c.StartsWith("RequestMicDetails", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void FailedDetailsRequestIsRetriedOnNextExpand()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            f.Backend.FailNext = new AudioCommandException("відмова");

            mic.ExpandButton!.Press();
            Assert.Single(f.Shell.Errors);
            mic.ExpandButton!.Press();
            mic.ExpandButton!.Press();

            Assert.Equal(2, f.Backend.Commands.Count(c => c.StartsWith("RequestMicDetails", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void BoostSliderAppearsOnlyForMicrophoneWithBoost()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var headset = f.Input(DemoIds.HeadsetMic);
            var webcam = f.Input(DemoIds.WebcamMic);

            headset.ExpandButton!.Press();
            webcam.ExpandButton!.Press();
            Sta.DoEvents();

            Assert.True(headset.MicSettings!.IsBoostVisible);
            Assert.False(headset.MicSettings.IsNoBoostVisible);
            Assert.Equal(0, headset.MicSettings.BoostSlider.Minimum);
            Assert.Equal(30, headset.MicSettings.BoostSlider.Maximum);
            Assert.Equal(10, headset.MicSettings.BoostSlider.Value);
            Assert.True(headset.MicSettings.BoostSlider.IsSnapToTickEnabled);
            Assert.Equal("+10 дБ", headset.MicSettings.BoostText);

            Assert.False(webcam.MicSettings!.IsBoostVisible);
            Assert.True(webcam.MicSettings.IsNoBoostVisible);
        });
    }

    [Fact]
    public void BoostChangeIsSentWhenDragEnds()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            mic.ExpandButton!.Press();
            Sta.DoEvents();
            var before = f.Backend.Commands.Count;

            var slider = mic.MicSettings!.BoostSlider;
            slider.DragStarted();
            slider.Value = 20;
            Assert.Equal(before, f.Backend.Commands.Count);

            slider.DragCompleted();
            Assert.Equal($"SetBoost {DemoIds.HeadsetMic} 20", f.Backend.Commands[^1]);
        });
    }

    [Fact]
    public void BoostFromDriverDoesNotSendCommandBack()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            mic.ExpandButton!.Press();
            Sta.DoEvents();
            var before = f.Backend.Commands.Count;

            var snapshot = f.Backend.Current;
            f.Backend.Replace(snapshot with
            {
                Devices = snapshot.Devices
                    .Select(d => d.Id != DemoIds.HeadsetMic ? d : d with { Details = d.Details! with { Boost = d.Details.Boost! with { LevelDb = 30 } } })
                    .ToArray(),
            });
            Sta.DoEvents();

            Assert.Equal(30, mic.MicSettings!.BoostSlider.Value);
            Assert.Equal(before, f.Backend.Commands.Count);
        });
    }

    [Fact]
    public void ListenControlsSendListenCommand()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            mic.ExpandButton!.Press();
            Sta.DoEvents();
            var panel = mic.MicSettings!;

            Assert.True(panel.IsListenVisible);
            Assert.False(panel.ListenCheck.IsChecked);
            Assert.Equal(4, panel.ListenTarget.Items.Count);
            Assert.Equal(0, panel.ListenTarget.SelectedIndex);

            panel.ListenCheck.IsChecked = true;
            panel.ListenCheck.Press();
            Assert.Equal($"SetListen {DemoIds.HeadsetMic} True default", f.Backend.Commands[^1]);
            Sta.DoEvents();
            Assert.True(panel.ListenCheck.IsChecked);

            panel.ListenTarget.SelectedIndex = 2;
            Assert.Equal($"SetListen {DemoIds.HeadsetMic} True {DemoIds.Speakers}", f.Backend.Commands[^1]);
            Sta.DoEvents();
            Assert.Equal(2, panel.ListenTarget.SelectedIndex);
        });
    }

    [Fact]
    public void ListenSectionIsHiddenWhenBackendCannotListen()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);
            mic.ExpandButton!.Press();
            Sta.DoEvents();

            var snapshot = f.Backend.Current;
            f.Backend.Replace(snapshot with
            {
                Devices = snapshot.Devices
                    .Select(d => d.Id != DemoIds.HeadsetMic ? d : d with { Details = d.Details! with { ListenSupported = false } })
                    .ToArray(),
            });
            Sta.DoEvents();

            Assert.False(mic.MicSettings!.IsListenVisible);
        });
    }

    [Fact]
    public void OtherWindowsSettingsLinkOpensInputProperties()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var mic = f.Input(DemoIds.HeadsetMic);

            mic.MicSettings!.Descendants<LinkButton>().Single().Press();

            Assert.Equal(["OpenInputProperties"], f.Shell.Calls);
        });
    }
}

public sealed class MixerMeterTests
{
    [Fact]
    public void AttachOpensOneTicketAndDetachClosesIt()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(attach: false);
            Assert.Equal(0, f.Backend.MeteringCount);

            f.View.Attach();
            f.View.Attach();
            Assert.Equal(1, f.Backend.MeteringCount);

            f.View.Detach();
            Assert.Equal(0, f.Backend.MeteringCount);
        });
    }

    [Fact]
    public void NoTicketWhenMetersAreOff()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(configure: s => s.Set(SettingKeys.ShowMeters, false));

            Assert.Equal(0, f.Backend.MeteringCount);
            Assert.Equal(Visibility.Collapsed, f.Output(DemoIds.Headphones).Row.Meter.Visibility);
        });
    }

    [Fact]
    public void TogglingOptionOpensAndClosesTicket()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            Assert.Equal(1, f.Backend.MeteringCount);

            f.Settings.Set(SettingKeys.ShowMeters, false);
            Sta.DoEvents();
            Assert.Equal(0, f.Backend.MeteringCount);
            Assert.Equal(Visibility.Collapsed, f.Output(DemoIds.Headphones).Row.Meter.Visibility);

            f.Settings.Set(SettingKeys.ShowMeters, true);
            Sta.DoEvents();
            Assert.Equal(1, f.Backend.MeteringCount);
            Assert.Equal(Visibility.Visible, f.Output(DemoIds.Headphones).Row.Meter.Visibility);
        });
    }

    [Fact]
    public void HiddenViewReleasesTicketAndShownViewTakesItAgain()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();

            f.View.Visibility = Visibility.Hidden;
            f.View.UpdateMeterTicket();
            Assert.Equal(0, f.Backend.MeteringCount);

            f.View.Visibility = Visibility.Visible;
            f.View.UpdateMeterTicket();
            Assert.Equal(1, f.Backend.MeteringCount);
        });
    }

    [Fact]
    public void PeaksReachMetersByKey()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            Sta.DoEvents();

            var headphones = f.Output(DemoIds.Headphones);
            Assert.Equal(PeakKeys.Device(DemoIds.Headphones), headphones.Row.Meter.Key);
            Assert.Equal(0.62f, headphones.Row.Meter.Level, 3);

            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
            Assert.Equal(PeakKeys.App(DemoIds.Headphones, DemoIds.ChromeKey), chrome.Row.Meter.Key);
            Assert.Equal(0.62f * 0.8f, chrome.Row.Meter.Level, 3);

            var cs2 = f.Output(DemoIds.Headphones).Apps.First(a => a.App.ProcessName == "cs2");
            Assert.Equal(0f, cs2.Row.Meter.Level);
        });
    }

    [Fact]
    public void MeterRisesAtOnceAndFallsGently()
    {
        Sta.Run(() =>
        {
            var meter = new LevelMeter();

            meter.SetLevel(0.9f);
            Assert.Equal(0.9f, meter.Level);

            meter.SetLevel(0.2f);
            Assert.Equal(0.84f, meter.Level, 3);

            meter.SetLevel(0.83f);
            Assert.Equal(0.83f, meter.Level, 3);

            meter.SetLevel(2f);
            Assert.Equal(1f, meter.Level);

            meter.Reset();
            Assert.Equal(0f, meter.Level);
        });
    }

    [Fact]
    public void PeaksAfterDetachDoNotTouchMeters()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            Sta.DoEvents();
            var meter = f.Output(DemoIds.Headphones).Row.Meter;
            f.View.Detach();
            Assert.Equal(0f, meter.Level);

            f.Backend.PushPeaks(FakeAudioPeaks(1f));
            Sta.DoEvents();

            Assert.Equal(0f, meter.Level);
        });
    }

    private static PeakSample FakeAudioPeaks(float level) =>
        new(new Dictionary<string, float> { [PeakKeys.Device(DemoIds.Headphones)] = level });
}
