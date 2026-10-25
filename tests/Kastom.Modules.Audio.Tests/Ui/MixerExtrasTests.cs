using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

public sealed class FlyoutPlacementTests
{
    // Монітор 1920×1080, панель 380×500, відступ 12 px.
    private static readonly Rect Bounds = new(0, 0, 1920, 1080);
    private static readonly Size Panel = new(380, 500);

    [Fact]
    public void TaskbarAtBottomPlacesPanelInBottomRightCorner()
    {
        var origin = MixerFlyout.ComputePlacement(new Rect(0, 0, 1920, 1040), Bounds, Panel);
        Assert.Equal(new Point(1920 - 380 - 12, 1040 - 500 - 12), origin);
    }

    [Fact]
    public void TaskbarAtTopPlacesPanelInTopRightCorner()
    {
        var origin = MixerFlyout.ComputePlacement(new Rect(0, 40, 1920, 1040), Bounds, Panel);
        Assert.Equal(new Point(1920 - 380 - 12, 40 + 12), origin);
    }

    [Fact]
    public void TaskbarAtLeftPlacesPanelInBottomLeftCorner()
    {
        var origin = MixerFlyout.ComputePlacement(new Rect(60, 0, 1860, 1080), Bounds, Panel);
        Assert.Equal(new Point(60 + 12, 1080 - 500 - 12), origin);
    }

    [Fact]
    public void TaskbarAtRightPlacesPanelInBottomRightCorner()
    {
        var origin = MixerFlyout.ComputePlacement(new Rect(0, 0, 1860, 1080), Bounds, Panel);
        Assert.Equal(new Point(1860 - 380 - 12, 1080 - 500 - 12), origin);
    }

    [Fact]
    public void HiddenTaskbarFallsBackToBottomRight()
    {
        var origin = MixerFlyout.ComputePlacement(Bounds, Bounds, Panel);
        Assert.Equal(new Point(1920 - 380 - 12, 1080 - 500 - 12), origin);
    }

    [Fact]
    public void SecondMonitorWithOffsetKeepsItsOwnCoordinates()
    {
        var bounds = new Rect(1920, 0, 2560, 1440);
        var work = new Rect(1920, 0, 2560, 1400);
        var origin = MixerFlyout.ComputePlacement(work, bounds, new Size(475, 700));
        Assert.Equal(new Point(1920 + 2560 - 475 - 12, 1400 - 700 - 12), origin);
    }

    [Fact]
    public void TallPanelStaysInsideWorkArea()
    {
        var origin = MixerFlyout.ComputePlacement(new Rect(0, 0, 1920, 1040), Bounds, new Size(380, 1100));
        Assert.Equal(0, origin.Y);
    }

    [Fact]
    public void FlyoutIsCreatedHiddenWithPanelProperties()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(attach: false);
            var flyout = new MixerFlyout(f.Host);

            Assert.False(flyout.IsVisible);
            Assert.Equal(WindowStyle.None, flyout.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, flyout.ResizeMode);
            Assert.False(flyout.ShowInTaskbar);
            Assert.True(flyout.Topmost);
            Assert.Equal(380, flyout.Width);
            Assert.Equal(SizeToContent.Height, flyout.SizeToContent);
            Assert.Same(f.Host, flyout.Host);
            Assert.Equal(0, f.Backend.MeteringCount);
        });
    }
}

public sealed class IconCacheTests
{
    [Fact]
    public void MissingFileGivesNull()
    {
        Sta.Run(() =>
        {
            Assert.Null(IconCache.Get(@"C:\no-such-folder\no-such-app.exe"));
            Assert.Null(IconCache.Get(null));
            Assert.Null(IconCache.Get("  "));
            Assert.Null(IconCache.Get("@{Microsoft.Windows.Photos_8wekyb3d8bbwe?ms-resource://Photos/Resources/AppName}"));
            Assert.Null(IconCache.Get("ms-resource:AppName"));
        });
    }

    [Fact]
    public void ExistingExecutableGivesFrozenIcon()
    {
        Sta.Run(() =>
        {
            var icon = IconCache.Get(@"C:\Windows\explorer.exe");

            Assert.NotNull(icon);
            Assert.True(icon!.IsFrozen);
            Assert.Same(icon, IconCache.Get(@"C:\Windows\explorer.exe"));
        });
    }

    [Fact]
    public void ResourceReferenceWithEnvironmentVariableIsResolved()
    {
        Sta.Run(() =>
        {
            Assert.NotNull(IconCache.Get(@"%windir%\system32\shell32.dll,-3"));
            Assert.NotNull(IconCache.Get("\"%windir%\\explorer.exe\""));
        });
    }
}

public sealed class MixerRenderTests
{
    [Theory]
    [InlineData(MixerMode.Page, 720, 1400)]
    [InlineData(MixerMode.Flyout, 380, 900)]
    [InlineData(MixerMode.Widget, 320, 170)]
    public void RendersOffscreenWithoutExceptions(MixerMode mode, int width, int height)
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(mode);
            foreach (var mic in f.View.InputCards) mic.ExpandButton!.Press();
            Sta.DoEvents();

            var bitmap = Render(f.View, width, height);

            Assert.Equal(width, bitmap.PixelWidth);
            Assert.Equal(height, bitmap.PixelHeight);
        });
    }

    [Theory]
    [InlineData(MixerMode.Page)]
    [InlineData(MixerMode.Flyout)]
    [InlineData(MixerMode.Widget)]
    public void RendersUnavailableAndEmptyStates(MixerMode mode)
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create(mode, start: false);
            Render(f.View, 400, 300);

            f.Backend.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            f.Backend.Replace(new AudioSnapshot([]));
            Sta.DoEvents();
            Render(f.View, 400, 300);
        });
    }

    [Fact]
    public void EmptySnapshotShowsGraySectionLines()
    {
        Sta.Run(() =>
        {
            var f = MixerFixture.Create();
            f.Backend.Replace(new AudioSnapshot([]));
            Sta.DoEvents();

            Assert.Empty(f.View.OutputCards);
            Assert.Empty(f.View.InputCards);
            Assert.False(f.View.IsUnavailableShown);
            Assert.Contains(f.View.Descendants<System.Windows.Controls.TextBlock>(), t => t.Text == "Немає пристроїв виводу" && t.Visibility == Visibility.Visible);
            Assert.Contains(f.View.Descendants<System.Windows.Controls.TextBlock>(), t => t.Text == "Немає мікрофонів" && t.Visibility == Visibility.Visible);
        });
    }

    private static RenderTargetBitmap Render(FrameworkElement view, int width, int height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        return bitmap;
    }
}
