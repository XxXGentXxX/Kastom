using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Дві плитки внизу панелі біля годинника: «Звук» (усі пристрої виводу) і «Мікрофони» (усі мікрофони). Плитки не належать
/// карткам пристроїв і є також у згорнутому мікшері.
/// </summary>
internal sealed class QuickTiles : Grid
{
    public QuickTiles(MixerContext ctx)
    {
        Sound = new QuickTile(ctx, AudioFlow.Render);
        Mics = new QuickTile(ctx, AudioFlow.Capture);

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(Mics, 2);
        Children.Add(Sound);
        Children.Add(Mics);
    }

    public QuickTile Sound { get; }

    public QuickTile Mics { get; }

    public void Update(AudioSnapshot snapshot)
    {
        Sound.Update(snapshot);
        Mics.Update(snapshot);
    }
}

/// <summary>
/// Плитка: велика кнопка з гліфом, під нею перемикач «Увімкнено/Вимкнено». Увімкнено, якщо хоч один пристрій цього виду
/// не вимкнено; клік вимикає всі, коли увімкнено, інакше вмикає всі. Кнопка й перемикач роблять те саме.
/// </summary>
internal sealed class QuickTile : Border
{
    private readonly MixerContext _ctx;
    private readonly AudioFlow _flow;
    private readonly TextBlock _glyph;
    private bool? _shownOff;

    public QuickTile(MixerContext ctx, AudioFlow flow)
    {
        _ctx = ctx;
        _flow = flow;
        var isMic = flow == AudioFlow.Capture;

        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8);
        SetResourceReference(BackgroundProperty, ThemeKeys.CardBackground);
        SetResourceReference(BorderBrushProperty, ThemeKeys.CardStroke);

        _glyph = MixerUi.Glyph(isMic ? Glyphs.Microphone : Glyphs.Speaker, 28);
        var title = MixerUi.Text(isMic ? MixerStrings.TileMics : MixerStrings.TileSound, 13);
        title.Margin = new Thickness(0, 4, 0, 0);
        title.HorizontalAlignment = HorizontalAlignment.Center;

        Button = new Button
        {
            Height = 68,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel { Children = { _glyph, title } },
        };
        Toggle = new CheckBox
        {
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(Toggle, isMic ? MixerStrings.TileMicsToggleName : MixerStrings.TileSoundToggleName);

        Button.Click += (_, _) => Execute();
        // CheckBox перемикається сам, а справжній стан після команди покаже перемальовування за знімком бекенда.
        Toggle.Click += (_, _) => Execute();

        Child = new StackPanel { Children = { Button, Toggle } };
        Update(AudioSnapshot.Empty);
    }

    /// <summary>Велика кнопка з гліфом.</summary>
    public Button Button { get; }

    /// <summary>Перемикач під кнопкою.</summary>
    public ToggleButton Toggle { get; }

    /// <summary>Хоч один пристрій цього виду увімкнений (не вимкнено).</summary>
    public bool IsOn { get; private set; }

    internal string GlyphText => _glyph.Text;

    /// <summary>Гліф червоний (SystemFillColorCriticalBrush): пристрої є, але всі вимкнено.</summary>
    internal bool IsGlyphCritical => _shownOff == true;

    internal string ToggleText => (string)Toggle.Content;

    public void Update(AudioSnapshot snapshot)
    {
        var devices = DevicesOf(snapshot);
        IsOn = devices.Any(d => !d.IsMuted);

        Button.IsEnabled = Toggle.IsEnabled = devices.Count > 0;
        Toggle.IsChecked = IsOn;
        Toggle.Content = IsOn ? MixerStrings.TileOn : MixerStrings.TileOff;

        // Червоний гліф — коли пристрої є, але всі вимкнено; без пристроїв кнопка просто недоступна.
        var off = devices.Count > 0 && !IsOn;
        if (_shownOff != off)
        {
            _shownOff = off;
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, off ? ThemeKeys.Critical : ThemeKeys.TextPrimary);
        }

        var action = ActionName(turnOff: IsOn);
        AutomationProperties.SetName(Button, action);
        Button.ToolTip = action;
    }

    private List<AudioDevice> DevicesOf(AudioSnapshot snapshot) =>
        (_flow == AudioFlow.Render ? snapshot.Outputs : snapshot.Inputs).ToList();

    private string ActionName(bool turnOff) => _flow == AudioFlow.Render
        ? turnOff ? MixerStrings.TileSoundMuteName : MixerStrings.TileSoundUnmuteName
        : turnOff ? MixerStrings.TileMicsMuteName : MixerStrings.TileMicsUnmuteName;

    // Стан беремо зі свіжого знімка бекенда, а не з того, що намальовано: кнопку могли натиснути до перемальовування.
    private void Execute()
    {
        var devices = DevicesOf(_ctx.Backend.Current);
        if (devices.Count == 0) return;

        var mute = devices.Any(d => !d.IsMuted);
        var ids = devices.Select(d => d.Id).ToList();
        _ = _ctx.Run(() => SetAll(ids, mute), Strings.OsdCommandFailed);
    }

    // Помилка одного пристрою не зупиняє решту; про першу помилку повідомляє Run.
    private async Task SetAll(IReadOnlyList<string> ids, bool muted)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var id in ids)
        {
            try
            {
                await _ctx.Backend.SetDeviceMuteAsync(id, muted);
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        failure?.Throw();
    }
}
