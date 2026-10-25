using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Tray;

namespace Kastom.Modules.Audio.Tests.Module;

public sealed class TrayIconRendererTests
{
    public static TheoryData<TrayGlyph, bool, bool, int> AllCases()
    {
        var data = new TheoryData<TrayGlyph, bool, bool, int>();
        foreach (var glyph in Enum.GetValues<TrayGlyph>())
        {
            foreach (var badge in new[] { false, true })
            {
                foreach (var light in new[] { false, true })
                {
                    foreach (var size in new[] { 16, 24, 32 }) data.Add(glyph, badge, light, size);
                }
            }
        }

        return data;
    }

    private static List<System.Drawing.Color> OpaquePixels(TrayGlyph glyph, bool badge, bool light, int size)
    {
        List<System.Drawing.Color> pixels = [];
        Sta.Run(() =>
        {
            using var icon = TrayIconRenderer.Render(glyph, badge, light, size);
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
    public void GlyphFillsTheRightSizeWithVisiblePixels(TrayGlyph glyph, bool badge, bool light, int size)
    {
        var pixels = OpaquePixels(glyph, badge, light, size);

        // Найтонший гліф (динамік без хвиль) займає не менше кількох відсотків площі; решта — прозорість.
        Assert.True(pixels.Count >= size * size / 20, $"Непрозорих пікселів лише {pixels.Count}.");
        Assert.True(pixels.Count < size * size * 3 / 4, $"Значок майже суцільний: {pixels.Count} пікселів.");
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void RedPixelsAppearOnlyWithTheMicBadge(TrayGlyph glyph, bool badge, bool light, int size)
    {
        var red = OpaquePixels(glyph, badge, light, size).Count(p => p.R >= p.G + 60 && p.R >= p.B + 60);

        // Коло позначки — хоча б чверть площі кола діаметром size / 2, навіть на 16 px.
        if (badge) Assert.True(red >= size * size / 24, $"Червоних пікселів лише {red}.");
        else Assert.Equal(0, red);
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void BadgeSitsInTheBottomRightCorner(TrayGlyph glyph, bool badge, bool light, int size)
    {
        if (!badge) return;

        Sta.Run(() =>
        {
            using var icon = TrayIconRenderer.Render(glyph, badge, light, size);
            using var bitmap = icon.ToBitmap();
            var reds = new List<(int X, int Y)>();
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.A >= 200 && p.R >= p.G + 60 && p.R >= p.B + 60) reds.Add((x, y));
                }
            }

            Assert.NotEmpty(reds);
            Assert.True(reds.Min(p => p.X) >= size / 2 - 1, "Позначка виходить за праву половину.");
            Assert.True(reds.Min(p => p.Y) >= size / 2 - 1, "Позначка виходить за нижню половину.");
            Assert.True(reds.Max(p => p.X) >= size - 2 && reds.Max(p => p.Y) >= size - 2, "Позначка не в самому куті.");
        });
    }

    [Theory]
    [InlineData(TrayGlyph.Muted, false)]
    [InlineData(TrayGlyph.Volume3, false)]
    [InlineData(TrayGlyph.Muted, true)]
    [InlineData(TrayGlyph.Volume3, true)]
    public void GlyphColourFollowsTheTaskbarTheme(TrayGlyph glyph, bool light)
    {
        var pixels = OpaquePixels(glyph, badge: false, light, 32);

        // Icon.ToBitmap віддає краї з уже помноженим на прозорість кольором: повертаємо його до справжнього.
        Assert.All(pixels, p =>
        {
            var channels = new[] { p.R, p.G, p.B }.Select(c => c * 255 / p.A).ToList();
            Assert.True(light ? channels.All(c => c < 40) : channels.All(c => c > 230), $"Колір {p}.");
        });
    }

    [Fact]
    public void BadgeIsRedOnBothThemesButDifferentShades()
    {
        var dark = OpaquePixels(TrayGlyph.Volume2, badge: true, light: false, 32);
        var light = OpaquePixels(TrayGlyph.Volume2, badge: true, light: true, 32);

        Assert.Contains(dark, p => p.R > 240 && p.G is > 90 and < 125);
        Assert.Contains(light, p => p.R is > 180 and < 210 && p.G < 60);
    }

    [Fact]
    public void BadgeChangesThePictureButNotItsSize()
    {
        var plain = OpaquePixels(TrayGlyph.Volume3, badge: false, light: false, 16);
        var badged = OpaquePixels(TrayGlyph.Volume3, badge: true, light: false, 16);

        Assert.NotEqual(plain.Count, badged.Count);
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
        var (glyph, text) = TrayLogic.DescribeSound(Output(volume));

        Assert.Equal(expected, glyph);
        Assert.Equal(Strings.Format(Strings.TrayVolumeTooltipFormat, "Навушники", VolumeMath.ToPercent(volume)), text);
    }

    [Fact]
    public void MutedAndMissingOutputsShowTheMutedGlyph()
    {
        var muted = TrayLogic.DescribeSound(Output(0.8f, muted: true));
        var none = TrayLogic.DescribeSound(null);

        Assert.Equal(TrayGlyph.Muted, muted.Glyph);
        Assert.Equal(Strings.Format(Strings.TrayMutedTooltipFormat, "Навушники"), muted.Text);
        Assert.Equal(TrayGlyph.Muted, none.Glyph);
        Assert.Equal(Strings.TrayNoDevice, none.Text);
    }

    private static AudioDevice Mic(bool muted = false, string name = "Мікрофон (HyperX)") => new()
    {
        Id = "mic",
        Flow = AudioFlow.Capture,
        Name = name,
        Volume = 0.7f,
        IsMuted = muted,
        IsDefault = true,
    };

    [Theory]
    [InlineData(false, "Мікрофон увімкнено: Мікрофон (HyperX)")]
    [InlineData(true, "Мікрофон вимкнено: Мікрофон (HyperX)")]
    public void MicLineFollowsMute(bool muted, string expected) =>
        Assert.Equal(expected, TrayLogic.DescribeMic(Mic(muted)));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IconIsVisibleByTheSettingEvenWithoutDevice(bool enabled, bool visible)
    {
        Assert.Equal(visible, TrayLogic.Icon(enabled, true, Output(0.5f), Mic()).Visible);
        Assert.Equal(visible, TrayLogic.Icon(enabled, true, null, null).Visible);
        Assert.Equal(TrayGlyph.Muted, TrayLogic.Icon(enabled, true, null, null).Glyph);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void BadgeNeedsSettingAndAMutedMic(bool badgeEnabled, bool hasMic, bool muted, bool badge)
    {
        var state = TrayLogic.Icon(true, badgeEnabled, Output(0.5f), hasMic ? Mic(muted) : null);

        Assert.Equal(badge, state.MicBadge);
    }

    [Fact]
    public void BadgeStaysOffWhenTheMicIsMissing()
    {
        Assert.False(TrayLogic.Icon(true, true, Output(0.5f), null).MicBadge);
    }

    [Fact]
    public void TooltipHasTwoLinesWithSoundAndMic()
    {
        var on = TrayLogic.Icon(true, true, Output(0.45f), Mic(muted: false));
        var off = TrayLogic.Icon(true, true, Output(0.45f), Mic(muted: true));

        Assert.Equal("Навушники: 45 %\nМікрофон увімкнено: Мікрофон (HyperX)", on.Tooltip);
        Assert.Equal("Навушники: 45 %\nМікрофон вимкнено: Мікрофон (HyperX)", off.Tooltip);
    }

    [Fact]
    public void TooltipWithoutAMicHasOnlyTheSoundLine()
    {
        Assert.Equal("Навушники: 45 %", TrayLogic.Icon(true, true, Output(0.45f), null).Tooltip);
        Assert.Equal(Strings.TrayNoDevice, TrayLogic.Icon(true, true, null, null).Tooltip);
        Assert.Equal(Strings.TrayNoDevice + "\nМікрофон увімкнено: Мікрофон (HyperX)", TrayLogic.Icon(true, true, null, Mic()).Tooltip);
    }

    [Fact]
    public void TooltipIsCutToWhatWindowsAccepts()
    {
        var longName = new string('Я', 300);

        var both = TrayLogic.Icon(true, true, Output(0.5f) with { Name = longName }, Mic(name: longName));
        var longSound = TrayLogic.Icon(true, true, Output(0.5f) with { Name = longName }, Mic());
        var longMic = TrayLogic.Icon(true, true, Output(0.5f), Mic(name: longName));
        var soundOnly = TrayLogic.Icon(true, true, Output(0.5f) with { Name = longName }, null);

        Assert.Equal(127, both.Tooltip.Length);
        Assert.Equal(2, both.Tooltip.Split('\n').Length);
        Assert.All(both.Tooltip.Split('\n'), line => Assert.True(line.Length > 40));
        Assert.True(longSound.Tooltip.Length <= 127);
        Assert.True(longMic.Tooltip.Length <= 127);
        Assert.Equal(127, soundOnly.Tooltip.Length);

        // Короткий другий рядок цілий, довгий перший віддає йому місце.
        Assert.EndsWith("\nМікрофон увімкнено: Мікрофон (HyperX)", longSound.Tooltip);
        Assert.StartsWith("Навушники: 50 %\n", longMic.Tooltip);

        Assert.Equal("коротка підказка", TrayLogic.FitTooltip("коротка підказка"));
        Assert.Equal(127, TrayLogic.FitTooltip(new string('x', 127)).Length);
        Assert.Equal(127, TrayLogic.FitTooltip(new string('x', 128)).Length);
        Assert.Equal("a\nb", TrayLogic.FitTooltip("a", "b"));
    }

    [Fact]
    public void RedrawOnlyWhenGlyphBadgeThemeOrSizeChange()
    {
        var drawn = new TrayLook(TrayGlyph.Volume2, MicBadge: false, Light: false, Size: 16);

        Assert.True(TrayLogic.NeedsRedraw(null, drawn));
        Assert.False(TrayLogic.NeedsRedraw(drawn, drawn with { }));
        Assert.True(TrayLogic.NeedsRedraw(drawn, drawn with { Glyph = TrayGlyph.Volume3 }));
        Assert.True(TrayLogic.NeedsRedraw(drawn, drawn with { MicBadge = true }));
        Assert.True(TrayLogic.NeedsRedraw(drawn, drawn with { Light = true }));
        Assert.True(TrayLogic.NeedsRedraw(drawn, drawn with { Size = 24 }));
    }

    [Fact]
    public void SmallVolumeChangeKeepsTheSameLookButChangesTheTooltip()
    {
        var before = TrayLogic.Icon(true, true, Output(0.45f), Mic());
        var after = TrayLogic.Icon(true, true, Output(0.46f), Mic());

        Assert.Equal(before.Glyph, after.Glyph);
        Assert.False(TrayLogic.NeedsRedraw(new TrayLook(before.Glyph, false, false, 16), new TrayLook(after.Glyph, false, false, 16)));
        Assert.True(TrayLogic.NeedsTooltip(before.Tooltip, after.Tooltip));
        Assert.False(TrayLogic.NeedsTooltip(after.Tooltip, after.Tooltip));
        Assert.True(TrayLogic.NeedsTooltip(null, after.Tooltip));
    }

    [Fact]
    public void FailingHandlerIsReportedNotThrown()
    {
        var shell = new FakeShell();
        var failure = new InvalidOperationException("зламалось");

        TrayLogic.RunGuarded(shell, () => throw failure);

        var error = Assert.Single(shell.Errors);
        Assert.Equal(Strings.OsdCommandFailed, error.Message);
        Assert.Same(failure, error.Exception);
    }

    [Fact]
    public void WorkingHandlerRunsAndReportsNothing()
    {
        var shell = new FakeShell();
        var calls = 0;

        TrayLogic.RunGuarded(shell, () => calls++);

        Assert.Equal(1, calls);
        Assert.Empty(shell.Errors);
    }

    [Fact]
    public void FailingReportDoesNotEscapeEither()
    {
        var shell = new ThrowingShell();

        TrayLogic.RunGuarded(shell, () => throw new InvalidOperationException("перша"));
    }

    private sealed class ThrowingShell : IAudioShell
    {
        public void OpenSoundSettings() { }

        public void OpenInputProperties() { }

        public void OpenOutputProperties() { }

        public void OpenAppVolumeSettings() { }

        public void OpenTaskbarSettings() { }

        public void OpenApp(AudioApp app) { }

        public void RevealApp(AudioApp app) { }

        public void ReportError(string userMessage, Exception? exception) => throw new InvalidOperationException("друга");
    }
}
