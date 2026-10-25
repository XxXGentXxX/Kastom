using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Рядок гучності: кнопка вимкнення, повзунок, відсотки, за потреби кнопка праворуч; під повзунком —
/// індикатор рівня. Спільний для пристроїв, програм, мікрофонів і віджета.
/// </summary>
internal sealed class VolumeRow : Grid
{
    private readonly MixerContext _ctx;
    private readonly TextBlock _percent = MixerUi.Text("", 13);

    public VolumeRow(MixerContext ctx, bool isMic, UIElement? trailing = null)
    {
        _ctx = ctx;
        Mute = new MuteButton(isMic);
        Slider = new VolumeSlider(() => ctx.Options.VolumeStepPercent)
        {
            Margin = new Thickness(8, 0, 8, 0),
            WheelNeedsFocus = ctx.Mode == MixerMode.Page,
        };
        Meter = new LevelMeter { Margin = new Thickness(8, -5, 8, 2), VerticalAlignment = VerticalAlignment.Top };

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _percent.TextAlignment = TextAlignment.Right;
        _percent.TextTrimming = TextTrimming.None;

        SetColumn(Slider, 1);
        SetColumn(_percent, 2);
        SetRow(Meter, 1);
        SetColumn(Meter, 1);
        Children.Add(Mute);
        Children.Add(Slider);
        Children.Add(_percent);
        Children.Add(Meter);

        if (trailing is not null)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetColumn(trailing, 3);
            Children.Add(trailing);
        }

        Slider.ValueChanged += (_, _) => ShowPercent();
        Slider.Settled = ctx.Refresh;
        Mute.Click += (_, _) => _ = MuteRequested?.Invoke(!Mute.IsMuted);
    }

    public MuteButton Mute { get; }

    public VolumeSlider Slider { get; }

    public LevelMeter Meter { get; }

    internal string PercentText => _percent.Text;

    /// <summary>Користувач натиснув кнопку вимкнення; аргумент — бажаний стан «вимкнено».</summary>
    public Func<bool, Task>? MuteRequested { get; set; }

    public void Update(string name, float volume, bool muted)
    {
        Slider.SetVolume(volume);
        Mute.Update(name, SliderPercent, muted);
        ShowPercent();
        AutomationProperties.SetName(Slider, MixerStrings.Format(MixerStrings.VolumeNameFormat, name));
        Meter.Visibility = _ctx.Options.ShowMeters ? Visibility.Visible : Visibility.Collapsed;
    }

    private int SliderPercent => (int)Math.Round(Slider.Value);

    private void ShowPercent()
    {
        _percent.Text = MixerUi.Percent(Slider.Value);
        Mute.SetPercent(SliderPercent);
    }
}
