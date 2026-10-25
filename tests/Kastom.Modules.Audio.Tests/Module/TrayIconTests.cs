using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Tray;

namespace Kastom.Modules.Audio.Tests.Module;

public sealed class TrayIconRendererTests
{
    public static TheoryData<TrayGlyph, bool, int> AllCases()
    {
        var data = new TheoryData<TrayGlyph, bool, int>();
        foreach (var glyph in Enum.GetValues<TrayGlyph>())
        {
            foreach (var light in new[] { false, true })
            {
                foreach (var size in new[] { 16, 24, 32 }) data.Add(glyph, light, size);
            }
        }

        return data;
    }

    private static List<System.Drawing.Color> OpaquePixels(TrayGlyph glyph, bool light, int size)
    {
        List<System.Drawing.Color> pixels = [];
        Sta.Run(() =>
        {
            using var icon = TrayIconRenderer.Render(glyph, light, size);
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
            using var bitmap = icon.ToBitmap();
            Assert.Equal(size, bitmap.Width);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A >= 200) pixels.Add(pixel);
                }
            }
        });
        return pixels;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void GlyphFillsTheRightSizeWithVisiblePixels(TrayGlyph glyph, bool light, int size)
    {
        var pixels = OpaquePixels(glyph, light, size);

        // Найтонший гліф (мікрофон) займає не менше кількох відсотків площі; решта — прозорість.
        Assert.True(pixels.Count >= size * size / 20, $"Непрозорих пікселів лише {pixels.Count}.");
        Assert.True(pixels.Count < size * size * 3 / 4, $"Значок майже суцільний: {pixels.Count} пікселів.");
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void OnlyTheMutedMicHasRedPixels(TrayGlyph glyph, bool light, int size)
    {
        var red = OpaquePixels(glyph, light, size).Count(p => p.R >= p.G + 60 && p.R >= p.B + 60);

        if (glyph == TrayGlyph.MicOff) Assert.True(red >= size, $"Червоних пікселів лише {red}.");
        else Assert.Equal(0, red);
    }

    [Theory]
    [InlineData(TrayGlyph.Mic, false)]
    [InlineData(TrayGlyph.Volume3, false)]
    [InlineData(TrayGlyph.Mic, true)]
    [InlineData(TrayGlyph.Volume3, true)]
    public void GlyphColourFollowsTheTaskbarTheme(TrayGlyph glyph, bool light)
    {
        var pixels = OpaquePixels(glyph, light, 32);

        // Icon.ToBitmap віддає краї з уже помноженим на прозорість кольором: повертаємо його до справжнього.
        Assert.All(pixels, p =>
        {
            var channels = new[] { p.R, p.G, p.B }.Select(c => c * 255 / p.A).ToList();
            Assert.True(light ? channels.All(c => c < 40) : channels.All(c => c > 230), $"Колір {p}.");
        });
    }

    [Fact]
    public void MutedMicIsRedOnBothThemesButDifferentShades()
    {
        var dark = OpaquePixels(TrayGlyph.MicOff, light: false, 32);
        var light = OpaquePixels(TrayGlyph.MicOff, light: true, 32);

        Assert.Contains(dark, p => p.R > 240 && p.G is > 90 and < 125);
        Assert.Contains(light, p => p.R is > 180 and < 210 && p.G < 60);
    }
}

public sealed class AudioTrayIconsTests
{
    private static AudioDevice Output(float volume, bool muted = false) => new()
    {
        Id = "out",
        Flow = AudioFlow.Render,
        Name = "Навушники",
        Volume = volume,
        IsMuted = muted,
        IsDefault = true,
    };

    [Theory]
    [InlineData(0f, TrayGlyph.Volume0)]
    [InlineData(0.004f, TrayGlyph.Volume0)]
    [InlineData(0.01f, TrayGlyph.Volume1)]
    [InlineData(0.32f, TrayGlyph.Volume1)]
    [InlineData(0.33f, TrayGlyph.Volume2)]
    [InlineData(0.65f, TrayGlyph.Volume2)]
    [InlineData(0.66f, TrayGlyph.Volume3)]
    [InlineData(1f, TrayGlyph.Volume3)]
    public void SoundGlyphFollowsVolume(float volume, TrayGlyph expected)
    {
        var (glyph, text) = AudioTrayIcons.DescribeSound(Output(volume));

        Assert.Equal(expected, glyph);
        Assert.Equal(Strings.Format(Strings.TrayVolumeTooltipFormat, "Навушники", VolumeMath.ToPercent(volume)), text);
    }

    [Fact]
    public void MutedAndMissingOutputsShowTheMutedGlyph()
    {
        var muted = AudioTrayIcons.DescribeSound(Output(0.8f, muted: true));
        var none = AudioTrayIcons.DescribeSound(null);

        Assert.Equal(TrayGlyph.Muted, muted.Glyph);
        Assert.Equal(Strings.Format(Strings.TrayMutedTooltipFormat, "Навушники"), muted.Text);
        Assert.Equal(TrayGlyph.Muted, none.Glyph);
        Assert.Equal(Strings.TrayNoDevice, none.Text);
    }
}
