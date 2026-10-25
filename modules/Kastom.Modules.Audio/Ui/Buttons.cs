using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Кнопка без рамки: прозора, підсвічується під курсором. Шаблон власний, щоб значкові кнопки не мали
/// важкої заливки стандартної кнопки Fluent.
/// </summary>
internal class SubtleButton : Button
{
    // Шаблон прив'язаний до потоку, що його створив, тож кеш на потік.
    [ThreadStatic]
    private static ControlTemplate? _template;

    public SubtleButton()
    {
        Template = _template ??= CreateTemplate();
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        SetResourceReference(BackgroundProperty, ThemeKeys.Subtle);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Background = Brushes.Transparent;
    }

    private static ControlTemplate CreateTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Border), "Root");
        root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        root.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.4));
        template.Triggers.Add(disabled);
        return template;
    }
}

/// <summary>Кнопка вимкнення звуку 32×32 з гліфом, який відповідає гучності (мікрофон — завжди мікрофон).</summary>
internal sealed class MuteButton : SubtleButton
{
    private readonly bool _isMic;
    private readonly TextBlock _glyph = MixerUi.Glyph(Glyphs.Speaker, 16);
    private readonly Line _strike;
    private bool? _shownMuted;
    private int _percent;

    public MuteButton(bool isMic)
    {
        _isMic = isMic;
        Width = 32;
        Height = 32;

        _strike = new Line
        {
            X1 = 1,
            Y1 = 15,
            X2 = 15,
            Y2 = 1,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _strike.SetResourceReference(Shape.StrokeProperty, ThemeKeys.Critical);
        Content = new Grid { Width = 16, Height = 16, Children = { _glyph, _strike } };
    }

    public bool IsMuted { get; private set; }

    internal string GlyphText => _glyph.Text;

    internal bool StrikeVisible => _strike.Visibility == Visibility.Visible;

    public void Update(string name, int percent, bool muted)
    {
        IsMuted = muted;
        _percent = percent;
        ApplyGlyph();
        if (_shownMuted != muted)
        {
            _shownMuted = muted;
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, muted ? ThemeKeys.Critical : ThemeKeys.TextPrimary);
        }

        AutomationProperties.SetName(this, MixerStrings.Format(muted ? MixerStrings.UnmuteNameFormat : MixerStrings.MuteNameFormat, name));
        ToolTip = muted ? MixerStrings.UnmuteTooltip : MixerStrings.MuteTooltip;
    }

    /// <summary>Гліф гучності йде за повзунком і під час перетягування, коли решта кнопки не змінюється.</summary>
    public void SetPercent(int percent)
    {
        if (_percent == percent) return;
        _percent = percent;
        ApplyGlyph();
    }

    private void ApplyGlyph()
    {
        _glyph.Text = _isMic ? Glyphs.Microphone : IsMuted ? Glyphs.Muted : Glyphs.Volume(_percent);
        _strike.Visibility = _isMic && IsMuted ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Кнопка «⋯» з назвою, за якою її читає екранний диктор.</summary>
internal sealed class MoreButton : SubtleButton
{
    public MoreButton()
    {
        Width = 32;
        Height = 32;
        Content = MixerUi.Glyph(Glyphs.More, 16);
        ToolTip = MixerStrings.MoreTooltip;
    }

    public void Update(string name) => AutomationProperties.SetName(this, MixerStrings.Format(MixerStrings.MoreNameFormat, name));
}

/// <summary>Кнопка-посилання: текст кольору акценту теми.</summary>
internal sealed class LinkButton : SubtleButton
{
    public LinkButton(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 13, TextDecorations = TextDecorations.Underline };
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
        Content = label;
        Padding = new Thickness(6, 4, 6, 4);
        HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, text);
    }
}
