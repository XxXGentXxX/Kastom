using System.Windows.Controls.Primitives;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Згорнутий мікшер (прохання Івана 2026-10-25): лише назва й головний повзунок кожного пристрою.</summary>
public sealed class CompactMixerTests
{
    [Fact]
    public void ExpandedByDefault() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        Assert.False(f.Options.Compact);
        Assert.All(f.View.OutputCards, c => Assert.True(c.AreAppsShown));
        Assert.True(f.Output(DemoIds.Speakers).AreButtonsShown);
    });

    [Theory]
    [InlineData(MixerMode.Page)]
    [InlineData(MixerMode.Flyout)]
    public void ButtonCollapsesEverythingButNameAndMainSlider(MixerMode mode) => Sta.Run(() =>
    {
        var f = MixerFixture.Create(mode);

        f.View.CompactButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
        Sta.DoEvents();

        Assert.True(f.Options.Compact);
        Assert.Equal(3, f.View.OutputCards.Count);
        Assert.Equal(2, f.View.InputCards.Count);
        Assert.All(f.View.OutputCards, c =>
        {
            Assert.False(c.AreAppsShown);
            Assert.False(c.AreButtonsShown);
            Assert.Equal(System.Windows.Visibility.Visible, c.Row.Visibility);
        });
        Assert.All(f.View.InputCards, c =>
        {
            Assert.Equal(System.Windows.Visibility.Collapsed, c.More!.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, c.ExpandButton!.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, c.MicSettings!.Visibility);
        });

        // Головний повзунок і далі керує пристроєм.
        Assert.True(f.Output(DemoIds.Headphones).IsDefaultBadgeVisible);
    });

    [Fact]
    public void SecondClickExpandsAgainAndRemembersInSettings() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(configure: s => s.Set(SettingKeys.Compact, true));
        Assert.All(f.View.OutputCards, c => Assert.False(c.AreAppsShown));

        f.View.CompactButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
        Sta.DoEvents();

        Assert.False(f.Settings.GetBool(SettingKeys.Compact));
        Assert.All(f.View.OutputCards, c => Assert.True(c.AreAppsShown));
    });

    [Fact]
    public void ChangeFromSettingsPageReachesOpenMixer() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Flyout);
        f.Settings.Set(SettingKeys.Compact, true);
        Sta.DoEvents();
        Assert.All(f.View.OutputCards, c => Assert.False(c.AreAppsShown));
    });
}
