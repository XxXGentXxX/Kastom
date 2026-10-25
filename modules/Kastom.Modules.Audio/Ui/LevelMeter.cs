using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Індикатор рівня звуку: доріжка 4 px із заливкою, яку ЗБІЛЬШУЄ ScaleTransform (розкладка не перераховується).
/// Швидко росте, плавно спадає.
/// </summary>
internal sealed class LevelMeter : Border
{
    /// <summary>На скільки найбільше спадає рівень за одне оновлення (оновлення ≈30 на секунду).</summary>
    private const float MaxFall = 0.06f;

    private readonly ScaleTransform _scale = new(0, 1);

    public LevelMeter()
    {
        Height = 4;
        CornerRadius = new CornerRadius(2);
        ClipToBounds = true;
        IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, ThemeKeys.Subtle);

        var fill = new Border
        {
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = _scale,
            RenderTransformOrigin = new Point(0, 0.5),
        };
        fill.SetResourceReference(BackgroundProperty, ThemeKeys.Accent);
        Child = fill;
    }

    /// <summary>Ключ у <see cref="Model.PeakSample"/>, за яким індикатор бере рівень.</summary>
    public string Key { get; set; } = "";

    /// <summary>Рівень, який зараз показано, 0..1.</summary>
    public float Level { get; private set; }

    public void SetLevel(float peak)
    {
        var target = Math.Clamp(peak, 0f, 1f);
        SetShown(target >= Level ? target : Math.Max(target, Level - MaxFall));
    }

    public void Reset() => SetShown(0);

    private void SetShown(float level)
    {
        if (level == Level) return;
        Level = level;
        _scale.ScaleX = level;
    }
}
