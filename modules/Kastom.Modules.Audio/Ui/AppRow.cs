using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Рядок програми під пристроєм виводу: значок, назва, гучність, «⋯». На сторінці все в один рядок,
/// у панелі біля годинника назва стоїть над повзунком. «Системні звуки» відділені лінією зверху.
/// </summary>
internal sealed class AppRow : StackPanel
{
    private readonly MixerContext _ctx;
    private readonly Border _divider;
    private readonly Grid _body = new();
    private readonly Image _image = new() { Width = 20, Height = 20, Stretch = Stretch.Uniform };
    private readonly TextBlock _glyph = MixerUi.Glyph(Glyphs.App, 20);
    private readonly TextBlock _name = MixerUi.Text("", 13);
    private readonly MoreButton _more = new();
    private AudioApp _app;
    private string? _iconPath;
    private bool _iconResolved;

    public AppRow(MixerContext ctx, AudioApp app)
    {
        _ctx = ctx;
        _app = app;

        _divider = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Visibility = Visibility.Collapsed };
        _divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        var icon = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), Children = { _glyph, _image } };

        Row = new VolumeRow(ctx, isMic: false, trailing: _more);
        Row.Slider.VolumeRequested = volume =>
            ctx.Run(() => ctx.Backend.SetAppVolumeAsync(_app.DeviceId, _app.Key, volume), Strings.OsdCommandFailed);
        Row.MuteRequested = muted =>
            ctx.Run(() => ctx.Backend.SetAppMuteAsync(_app.DeviceId, _app.Key, muted), Strings.OsdCommandFailed);
        _more.Click += (_, _) => RowMenus.Open(_more, BuildMenu());

        var title = new DockPanel { LastChildFill = true, Children = { icon, _name } };
        DockPanel.SetDock(icon, Dock.Left);

        if (ctx.IsFlyout)
        {
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(Row, 1);
            Grid.SetRow(title, 0);
            title.Margin = new Thickness(0, 4, 0, 0);
        }
        else
        {
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(Row, 1);
            title.Margin = new Thickness(0, 0, 8, 0);
        }

        _body.Children.Add(title);
        _body.Children.Add(Row);
        Children.Add(_divider);
        Children.Add(_body);
    }

    public VolumeRow Row { get; }

    public AudioApp App => _app;

    internal bool IsDividerVisible => _divider.Visibility == Visibility.Visible;

    internal bool IsMoreVisible => _more.Visibility == Visibility.Visible;

    internal double BodyOpacity => _body.Opacity;

    internal MoreButton More => _more;

    internal ContextMenu BuildMenu() => RowMenus.BuildAppMenu(_ctx, _app);

    public void Update(AudioApp app)
    {
        _app = app;
        var name = !string.IsNullOrWhiteSpace(app.Name) ? app.Name : app.IsSystemSounds ? Strings.SystemSounds : Strings.UnknownApp;

        _name.Text = name;
        _name.ToolTip = name;
        ShowIcon(app);
        _divider.Visibility = app.IsSystemSounds ? Visibility.Visible : Visibility.Collapsed;
        _body.Opacity = app.IsMuted ? 0.55 : 1;

        Row.Meter.Key = PeakKeys.App(app.DeviceId, app.Key);
        Row.Update(name, app.Volume, app.IsMuted);

        _more.Visibility = RowMenus.HasAppMenu(_ctx, app) ? Visibility.Visible : Visibility.Collapsed;
        _more.Update(name);
    }

    private void ShowIcon(AudioApp app)
    {
        if (_iconResolved && _iconPath == app.IconPath) return;
        _iconResolved = true;
        _iconPath = app.IconPath;

        var source = app.IsSystemSounds ? null : IconCache.Get(app.IconPath);
        _image.Source = source;
        _image.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        _glyph.Text = app.IsSystemSounds ? Glyphs.SystemSounds : Glyphs.App;
        _glyph.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
