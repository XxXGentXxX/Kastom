using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Картка пристрою виводу (з рядками програм) або мікрофона (з розгортанням налаштувань). Живе між знімками:
/// <see cref="Update"/> лише міняє вміст.
/// </summary>
internal sealed class DeviceCard : Border
{
    private readonly MixerContext _ctx;
    private readonly bool _isMic;
    private readonly TextBlock _glyph;
    private readonly TextBlock _name = MixerUi.Text("", 14, semibold: true);
    private readonly Border _defaultBadge = Badge(MixerStrings.BadgeDefault);
    private readonly Border _communicationsBadge = Badge(MixerStrings.BadgeCommunications);
    private readonly Button _makeDefault = ActionButton(MixerStrings.MakeDefault);
    private readonly Button _makeCommunications = ActionButton(MixerStrings.MakeCommunications);
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _appsPanel = new() { Margin = new Thickness(28, 6, 0, 0) };
    private readonly Dictionary<string, AppRow> _apps = new();
    private readonly MoreButton? _more;
    private readonly SubtleButton? _expand;
    private readonly TextBlock? _chevron;
    private readonly MicSettingsPanel? _micSettings;
    private AudioDevice _device;
    private bool _expanded;
    private bool _detailsRequested;

    public DeviceCard(MixerContext ctx, AudioDevice device)
    {
        _ctx = ctx;
        _device = device;
        _isMic = device.Flow == AudioFlow.Capture;
        Id = device.Id;

        var flyout = ctx.IsFlyout;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(flyout ? 10 : 12);
        Margin = new Thickness(0, 0, 0, flyout ? 6 : 8);
        SetResourceReference(BackgroundProperty, ThemeKeys.CardBackground);
        SetResourceReference(BorderBrushProperty, ThemeKeys.CardStroke);

        _glyph = MixerUi.Glyph(_isMic ? Glyphs.Microphone : Glyphs.Speaker, 20);
        _glyph.Margin = new Thickness(0, 0, 8, 0);

        var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(_defaultBadge);
        badges.Children.Add(_communicationsBadge);
        _buttons.Children.Add(_makeDefault);
        _buttons.Children.Add(_makeCommunications);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_name, 1);
        Grid.SetColumn(badges, 2);
        header.Children.Add(_glyph);
        header.Children.Add(_name);
        header.Children.Add(badges);

        var stack = new StackPanel();
        stack.Children.Add(header);

        if (flyout)
        {
            // У вузькій панелі кнопки не вміщаються поруч із назвою: вони йдуть окремим рядком.
            _buttons.Margin = new Thickness(22, 6, 0, 0);
            stack.Children.Add(_buttons);
        }
        else
        {
            _buttons.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(_buttons, 3);
            header.Children.Add(_buttons);
        }

        _makeDefault.Click += (_, _) => _ = _ctx.Run(
            () => _ctx.Backend.SetDefaultDeviceAsync(Id, _ctx.Options.RolesFor(false)), Strings.OsdDefaultSwitchFailed);
        _makeCommunications.Click += (_, _) => _ = _ctx.Run(
            () => _ctx.Backend.SetDefaultDeviceAsync(Id, _ctx.Options.RolesFor(true)), Strings.OsdDefaultSwitchFailed);

        if (_isMic)
        {
            _more = new MoreButton();
            _more.Click += (_, _) => RowMenus.Open(_more, RowMenus.BuildMicMenu(_ctx, _device));
            Row = new VolumeRow(ctx, isMic: true, trailing: _more) { Margin = new Thickness(0, 4, 0, 0) };

            _chevron = MixerUi.Glyph(Glyphs.ChevronDown, 12);
            _chevron.Margin = new Thickness(6, 0, 0, 0);
            var label = MixerUi.Text(MixerStrings.MicSettings, 13);
            _expand = new SubtleButton
            {
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 0),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, _chevron } },
            };
            AutomationProperties.SetName(_expand, MixerStrings.MicSettings);
            _expand.Click += (_, _) => ToggleExpanded();
            _micSettings = new MicSettingsPanel(ctx, Id);

            stack.Children.Add(Row);
            stack.Children.Add(_expand);
            stack.Children.Add(_micSettings);
        }
        else
        {
            Row = new VolumeRow(ctx, isMic: false) { Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(Row);
            stack.Children.Add(_appsPanel);
        }

        Row.Slider.VolumeRequested = volume => _ctx.Run(() => _ctx.Backend.SetDeviceVolumeAsync(Id, volume), Strings.OsdCommandFailed);
        Row.MuteRequested = muted => _ctx.Run(() => _ctx.Backend.SetDeviceMuteAsync(Id, muted), Strings.OsdCommandFailed);

        Child = stack;
        Update(device);
    }

    public string Id { get; }

    public VolumeRow Row { get; }

    public AudioDevice Device => _device;

    /// <summary>Рядки програм у порядку на екрані.</summary>
    public IReadOnlyList<AppRow> Apps => _appsPanel.Children.OfType<AppRow>().ToList();

    internal Button MakeDefaultButton => _makeDefault;

    internal Button MakeCommunicationsButton => _makeCommunications;

    internal bool IsDefaultBadgeVisible => _defaultBadge.Visibility == Visibility.Visible;

    internal bool IsCommunicationsBadgeVisible => _communicationsBadge.Visibility == Visibility.Visible;

    internal string GlyphText => _glyph.Text;

    internal string NameText => _name.Text;

    internal MoreButton? More => _more;

    internal SubtleButton? ExpandButton => _expand;

    internal MicSettingsPanel? MicSettings => _micSettings;

    internal ContextMenu? BuildMicMenu() => _isMic ? RowMenus.BuildMicMenu(_ctx, _device) : null;

    public void Update(AudioDevice device)
    {
        _device = device;
        var name = string.IsNullOrWhiteSpace(device.Name) ? Strings.UnknownDevice : device.Name;

        _glyph.Text = _isMic ? Glyphs.Microphone : Glyphs.ForOutput(name);
        _name.Text = name;
        _name.ToolTip = name;

        var separate = _ctx.Options.SeparateCommunications;
        _defaultBadge.Visibility = Show(device.IsDefault);
        _communicationsBadge.Visibility = Show(separate && device.IsDefaultCommunications);
        _makeDefault.Visibility = Show(!device.IsDefault);
        _makeCommunications.Visibility = Show(separate && !device.IsDefaultCommunications);
        _buttons.Visibility = Show(!device.IsDefault || (separate && !device.IsDefaultCommunications));
        AutomationProperties.SetName(_makeDefault, MixerStrings.Format(MixerStrings.MakeDefaultNameFormat, name));
        _makeDefault.ToolTip = MixerStrings.Format(MixerStrings.MakeDefaultTooltipFormat, name);
        AutomationProperties.SetName(_makeCommunications, MixerStrings.Format(MixerStrings.MakeCommunicationsNameFormat, name));
        _makeCommunications.ToolTip = MixerStrings.Format(MixerStrings.MakeCommunicationsTooltipFormat, name);

        Row.Meter.Key = PeakKeys.Device(device.Id);
        Row.Update(name, device.Volume, device.IsMuted);

        if (_isMic)
        {
            _more!.Update(name);
            _micSettings!.Update(device);
        }
        else
        {
            // «Системні звуки» завжди останні, решта — як у знімку.
            var visible = VisibleApps(device, a => _apps.TryGetValue(a.Key, out var row) && row.Row.Slider.IsBusy).ToList();
            var ordered = visible.Where(a => !a.IsSystemSounds).Concat(visible.Where(a => a.IsSystemSounds)).ToList();
            PanelSync.Apply(_appsPanel, _apps, ordered, a => a.Key, a => new AppRow(_ctx, a), (row, a) => row.Update(a));
        }
    }

    /// <summary>
    /// Програми, які показуємо під пристроєм. Windows тримає неактивний сеанс програми на кожному пристрої, куди та
    /// колись виводила звук (на ПК з трьома виходами Discord і «Системні звуки» стояли б під усіма трьома), тож під
    /// неосновним пристроєм — лише те, що зараз звучить або направлене туди вручну. Під основним ховаємо мовчазний
    /// сеанс програми, яку направлено на інший пристрій: інакше вона була б видна двічі. Рядок, чий повзунок
    /// зайнятий (перетягування, команда в дорозі), лишається, поки користувач ним керує, навіть коли програма замовкла.
    /// </summary>
    internal static IEnumerable<AudioApp> VisibleApps(AudioDevice device, Func<AudioApp, bool>? isBusy = null) =>
        device.Apps.Where(a => IsVisibleUnder(device, a) || isBusy?.Invoke(a) == true);

    private static bool IsVisibleUnder(AudioDevice device, AudioApp app) =>
        device.IsDefault
            ? app.IsActive || app.RoutedDeviceId is null || app.RoutedDeviceId == device.Id
            : app.RoutedDeviceId == device.Id || (app.IsActive && !app.IsSystemSounds);

    /// <summary>Індикатори рівня цієї картки (пристрій і його програми).</summary>
    public IEnumerable<LevelMeter> Meters => _apps.Values.Select(a => a.Row.Meter).Prepend(Row.Meter);

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        _micSettings!.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        _chevron!.Text = _expanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        if (!_expanded || _detailsRequested) return;

        // Драйвер опитуємо лише раз і лише коли про налаштування просять: це повільно й не завжди безпечно.
        _detailsRequested = true;
        _ = RequestDetails();
    }

    private async Task RequestDetails()
    {
        var succeeded = await _ctx.Run(() => _ctx.Backend.RequestMicDetailsAsync(Id), Strings.OsdCommandFailed);
        if (!succeeded) _detailsRequested = false;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static Border Badge(string text)
    {
        var label = MixerUi.Text(text, 11);
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextOnAccent);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label,
        };
        badge.SetResourceReference(BackgroundProperty, ThemeKeys.Accent);
        return badge;
    }

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        FontSize = 13,
        Padding = new Thickness(10, 3, 10, 4),
        Margin = new Thickness(6, 0, 0, 0),
    };
}
