using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

public sealed class MixerCommandTests
{
    [Fact]
    public void AppSliderAndMuteSendCommands()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            chrome.Row.Slider.Value = 30;
            chrome.Row.Mute.Press();

            Assert.Contains($"SetAppVolume {DemoIds.Headphones} {DemoIds.ChromeKey} {0.3f:0.###}", f.Backend.Commands);
            Assert.Contains($"SetAppMute {DemoIds.Headphones} {DemoIds.ChromeKey} True", f.Backend.Commands);
        });
    }

    [Fact]
    public void DeviceAndMicControlsSendCommands()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();

            f.Output(DemoIds.Speakers).Row.Slider.Value = 70;
            f.Output(DemoIds.Monitor).Row.Mute.Press();
            f.Input(DemoIds.HeadsetMic).Row.Mute.Press();
            f.Input(DemoIds.WebcamMic).Row.Mute.Press();

            Assert.Contains($"SetDeviceVolume {DemoIds.Speakers} {0.7f:0.###}", f.Backend.Commands);
            Assert.Contains($"SetDeviceMute {DemoIds.Monitor} False", f.Backend.Commands);
            Assert.Contains($"SetDeviceMute {DemoIds.HeadsetMic} True", f.Backend.Commands);
            Assert.Contains($"SetDeviceMute {DemoIds.WebcamMic} False", f.Backend.Commands);
        });
    }

    [Fact]
    public void ExternalChangeUpdatesSameRowInPlaceWithoutSendingBack()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
            Assert.Equal(80, chrome.Row.Slider.Value);
            Assert.Equal("80 %", chrome.Row.PercentText);

            f.Backend.Replace(f.WithApp(DemoIds.Headphones, DemoIds.ChromeKey, a => a with { Volume = 0.2f, IsMuted = true }));
            Sta.DoEvents();

            Assert.Same(chrome, f.App(DemoIds.Headphones, DemoIds.ChromeKey));
            Assert.Equal(20, chrome.Row.Slider.Value);
            Assert.Equal("20 %", chrome.Row.PercentText);
            Assert.True(chrome.Row.Mute.IsMuted);
            Assert.Equal(0.55, chrome.BodyOpacity);
            Assert.Empty(f.Backend.Commands);
        });
    }

    [Fact]
    public void ExternalChangesAreGroupedIntoOneUpdate()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            for (var volume = 10; volume <= 50; volume += 10)
                f.Backend.Replace(f.WithApp(DemoIds.Headphones, DemoIds.ChromeKey, a => a with { Volume = volume / 100f }));

            Assert.Equal(80, chrome.Row.Slider.Value);
            Sta.DoEvents();
            Assert.Equal(50, chrome.Row.Slider.Value);
        });
    }

    [Fact]
    public void SliderIgnoresBackendWhileDraggedAndCatchesUpAfterwards()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            chrome.Row.Slider.DragStarted();
            f.Backend.Replace(f.WithApp(DemoIds.Headphones, DemoIds.ChromeKey, a => a with { Volume = 0.1f }));
            Sta.DoEvents();
            Assert.Equal(80, chrome.Row.Slider.Value);

            chrome.Row.Slider.DragCompleted();
            Assert.Empty(f.Backend.Commands);
            f.Backend.Replace(f.WithApp(DemoIds.Headphones, DemoIds.ChromeKey, a => a with { Volume = 0.1f }));
            Sta.DoEvents();
            Assert.Equal(10, chrome.Row.Slider.Value);
        });
    }

    [Fact]
    public void DragSendsAtMostOneCommandPerBurstAndAlwaysTheFinalValue()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var slider = f.App(DemoIds.Headphones, DemoIds.ChromeKey).Row.Slider;
            slider.Clock = () => 0;

            slider.DragStarted();
            slider.Value = 10;
            slider.Value = 20;
            slider.Value = 30;
            Assert.Single(f.Backend.Commands);
            Assert.Contains($"{0.1f:0.###}", f.Backend.Commands[0]);

            slider.DragCompleted();
            Assert.Equal(2, f.Backend.Commands.Count);
            Assert.EndsWith($"{0.3f:0.###}", f.Backend.Commands[1]);
        });
    }

    [Fact]
    public void FailedVolumeCommandReturnsSliderToRealValue()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
            var failure = new AudioCommandException("відмова");
            f.Backend.FailNext = failure;

            chrome.Row.Slider.Value = 30;
            Sta.DoEvents();

            var error = Assert.Single(f.Shell.Errors);
            Assert.Equal(Strings.OsdCommandFailed, error.Message);
            Assert.Same(failure, error.Exception);
            Assert.Equal(80, chrome.Row.Slider.Value);
        });
    }

    [Fact]
    public void WheelOverSliderInFlyoutStepsVolumeAndIsHandled()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(MixerMode.Flyout);
            var slider = f.Output(DemoIds.Headphones).Row.Slider;

            var up = slider.Wheel(120);
            Assert.True(up.Handled);
            Assert.Equal(46, slider.Value);
            Assert.Contains($"SetDeviceVolume {DemoIds.Headphones} {0.46f:0.###}", f.Backend.Commands);

            Sta.DoEvents();
            f.Settings.Set(SettingKeys.VolumeStep, 5);
            var down = slider.Wheel(-120);
            Assert.True(down.Handled);
            Assert.Equal(45, slider.Value);
        });
    }

    [Theory]
    [InlineData(MixerMode.Flyout)]
    [InlineData(MixerMode.Widget)]
    public void WheelOverSliderOutsideThePageAlwaysStepsVolume(MixerMode mode)
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(mode);
            var slider = mode == MixerMode.Widget ? f.View.WidgetVolume!.Slider : f.Output(DemoIds.Headphones).Row.Slider;
            Assert.False(slider.IsKeyboardFocusWithin);

            var args = slider.Wheel(120);

            Assert.True(args.Handled);
            Assert.Equal(46, slider.Value);
        });
    }

    [Fact]
    public void WheelOverSliderOnThePageScrollsInsteadUnlessTheSliderHasFocus()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var slider = f.Output(DemoIds.Headphones).Row.Slider;
            Assert.False(slider.IsKeyboardFocusWithin);

            var args = slider.Wheel(120);

            Assert.False(args.Handled);
            Assert.Equal(45, slider.Value);
            Assert.DoesNotContain(f.Backend.Commands, c => c.StartsWith("SetDeviceVolume", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void WheelAppliesOnlyWithFocusWhenThePageScrolls(bool needsFocus, bool hasFocus, bool expected) =>
        Assert.Equal(expected, VolumeSlider.WheelApplies(needsFocus, hasFocus));

    [Fact]
    public void MakeDefaultUsesRolesFromOptions()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var speakers = f.Output(DemoIds.Speakers);

            Assert.Equal(System.Windows.Visibility.Collapsed, f.Output(DemoIds.Headphones).MakeDefaultButton.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, speakers.MakeDefaultButton.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, speakers.MakeCommunicationsButton.Visibility);

            speakers.MakeDefaultButton.Press();
            Assert.Equal($"SetDefaultDevice {DemoIds.Speakers} All", f.Backend.Commands[^1]);
        });
    }

    [Fact]
    public void SeparateCommunicationsSplitsButtonsAndBadges()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var headphones = f.Output(DemoIds.Headphones);
            var speakers = f.Output(DemoIds.Speakers);
            Assert.False(headphones.IsCommunicationsBadgeVisible);

            f.Settings.Set(SettingKeys.SeparateCommunications, true);
            Sta.DoEvents();

            Assert.True(headphones.IsCommunicationsBadgeVisible);
            Assert.True(speakers.MakeDefaultButton.Visibility == System.Windows.Visibility.Visible);
            Assert.True(speakers.MakeCommunicationsButton.Visibility == System.Windows.Visibility.Visible);

            speakers.MakeDefaultButton.Press();
            speakers.MakeCommunicationsButton.Press();
            Assert.Contains($"SetDefaultDevice {DemoIds.Speakers} Normal", f.Backend.Commands);
            Assert.Contains($"SetDefaultDevice {DemoIds.Speakers} Communications", f.Backend.Commands);
        });
    }

    [Fact]
    public void FailedDefaultSwitchReportsItsOwnMessage()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            f.Backend.FailNext = new AudioCommandException("відмова");

            f.Output(DemoIds.Speakers).MakeDefaultButton.Press();

            var error = Assert.Single(f.Shell.Errors);
            Assert.Equal(Strings.OsdDefaultSwitchFailed, error.Message);
        });
    }

    [Fact]
    public void FailedMuteReportsErrorWithoutThrowing()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            f.Backend.FailNext = new AudioCommandException("відмова");

            f.App(DemoIds.Headphones, DemoIds.ChromeKey).Row.Mute.Press();

            Assert.Single(f.Shell.Errors);
            Assert.Equal(Strings.OsdCommandFailed, f.Shell.Errors[0].Message);
        });
    }
}

public sealed class MixerMenuTests
{
    [Fact]
    public void OutputMenuHasDefaultAndEveryOutputAndRoutesApp()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
            var menu = chrome.BuildMenu();

            Assert.Equal(["Відкрити програму", "Показати у Провіднику", "Вивід (експериментально)", "Призначити на дію"], menu.Headers());
            var output = menu.Item("Вивід (експериментально)");
            Assert.Equal(
                ["Пристрій за замовчуванням", "Навушники (HyperX Cloud II)", "Динаміки (Realtek(R) Audio)", "LG ULTRAGEAR (NVIDIA High Definition Audio)"],
                output.Headers());
            Assert.True(output.Item("Пристрій за замовчуванням").IsChecked);
            Assert.False(output.Item("Динаміки (Realtek(R) Audio)").IsChecked);

            output.Item("Динаміки (Realtek(R) Audio)").Press();
            Assert.Equal($"SetAppOutput {DemoIds.Headphones} {DemoIds.ChromeKey} {DemoIds.Speakers}", f.Backend.Commands[^1]);
        });
    }

    [Fact]
    public void RoutedAppIsCheckedAndDefaultEntryResetsIt()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var spotify = f.App(DemoIds.Speakers, DemoIds.SpotifyKey);
            var output = spotify.BuildMenu().Item("Вивід (експериментально)");

            Assert.False(output.Item("Пристрій за замовчуванням").IsChecked);
            Assert.True(output.Item("Динаміки (Realtek(R) Audio)").IsChecked);

            output.Item("Пристрій за замовчуванням").Press();
            Assert.Equal($"SetAppOutput {DemoIds.Speakers} {DemoIds.SpotifyKey} default", f.Backend.Commands[^1]);
        });
    }

    [Fact]
    public void OutputMenuNeedsOptionAndBackendSupport()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            f.Settings.Set(SettingKeys.PerAppOutput, false);
            Assert.Equal(["Відкрити програму", "Показати у Провіднику", "Призначити на дію"], chrome.BuildMenu().Headers());

            f.Settings.Set(SettingKeys.PerAppOutput, true);
            f.Backend.Capabilities = new BackendCapabilities(true, false, true);
            Assert.Equal(["Відкрити програму", "Показати у Провіднику", "Призначити на дію"], chrome.BuildMenu().Headers());

            f.Backend.Capabilities = new BackendCapabilities(true, true, true);
            Assert.Equal(4, chrome.BuildMenu().Headers().Count());
        });
    }

    [Fact]
    public void SystemSoundsHasNoMenuButton()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var system = f.Output(DemoIds.Headphones).Apps[^1];

            Assert.True(system.App.IsSystemSounds);
            Assert.False(system.IsMoreVisible);
            Assert.Empty(system.BuildMenu().Items);
            Assert.True(f.App(DemoIds.Headphones, DemoIds.ChromeKey).IsMoreVisible);
        });
    }

    [Fact]
    public void FailedRoutingReportsRoutingMessage()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var menu = f.App(DemoIds.Headphones, DemoIds.ChromeKey).BuildMenu();
            f.Backend.FailNext = new AudioCommandException("відмова");

            menu.Item("Вивід (експериментально)").Item("Динаміки (Realtek(R) Audio)").Press();

            Assert.Equal(Strings.OsdAppRoutingFailed, Assert.Single(f.Shell.Errors).Message);
        });
    }

    [Fact]
    public void AssignAppToActionSlotChangesOption()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);

            Assert.Equal(
                ["Програма 1 — не вибрано", "Програма 2 — не вибрано", "Програма 3 — не вибрано"],
                chrome.BuildMenu().Item("Призначити на дію").Headers());

            chrome.BuildMenu().Item("Призначити на дію").Item("Програма 2 — не вибрано").Press();

            Assert.Equal("chrome", f.Options.AppSlot(2));
            Assert.Equal("", f.Options.AppSlot(1));
            Assert.Contains("Програма 2 — chrome", chrome.BuildMenu().Item("Призначити на дію").Headers());
        });
    }

    [Fact]
    public void AssignMicToActionSlotChangesOption()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            var menu = f.Input(DemoIds.HeadsetMic).BuildMicMenu()!;

            Assert.Equal(["Призначити на дію", "Параметри звуку Windows"], menu.Headers());
            Assert.Equal(["Мікрофон 1 — не вибрано", "Мікрофон 2 — не вибрано"], menu.Item("Призначити на дію").Headers());

            menu.Item("Призначити на дію").Item("Мікрофон 1 — не вибрано").Press();
            Assert.Equal("Мікрофон (HyperX Cloud II)", f.Options.MicSlot(1));
        });
    }

    [Fact]
    public void OutputCardsHaveNoMicMenu()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            Assert.Null(f.Output(DemoIds.Headphones).BuildMicMenu());
            Assert.NotNull(f.Output(DemoIds.Headphones).More);
            Assert.NotNull(f.Input(DemoIds.HeadsetMic).More);
        });
    }
}
