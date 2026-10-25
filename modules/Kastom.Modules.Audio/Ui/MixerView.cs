using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>Де показано мікшер.</summary>
public enum MixerMode
{
    /// <summary>Власна сторінка модуля в лаунчері (широка, усе розгорнуто).</summary>
    Page,

    /// <summary>Панель біля значка в області сповіщень (вузька).</summary>
    Flyout,

    /// <summary>Віджет: лише загальна гучність і мікрофон.</summary>
    Widget,
}

/// <summary>
/// Мікшер (З1–З10): показує знімок <c>Backend.Current</c> і живе з подій бекенда. Картки пристроїв і рядки програм
/// оновлюються на місці, без перебудови. Події бекенда приходять не з потоку інтерфейсу: ми тримаємо
/// останнє значення й плануємо одне оновлення.
/// </summary>
public sealed class MixerView : UserControl
{
    private readonly MixerContext _ctx;
    private readonly StackPanel _root = new();
    private readonly SubtleButton _compactButton = new();
    private readonly TextBlock _compactGlyph = MixerUi.Glyph(Glyphs.ChevronUp, 14);
    private readonly TextBlock _compactLabel = MixerUi.Text("", 13);
    private readonly TextBlock _unavailable = MixerUi.Text(MixerStrings.AudioUnavailable, 14, secondary: true);

    private readonly StackPanel _outputSection = new();
    private readonly StackPanel _outputPanel = new();
    private readonly TextBlock _noOutputs = MixerUi.Text(MixerStrings.NoOutputs, 13, secondary: true);
    private readonly Dictionary<string, DeviceCard> _outputCards = new();

    private readonly StackPanel _inputSection = new();
    private readonly StackPanel _inputPanel = new();
    private readonly TextBlock _noInputs = MixerUi.Text(MixerStrings.NoInputs, 13, secondary: true);
    private readonly Dictionary<string, DeviceCard> _inputCards = new();

    // Віджет: лише пристрій виводу за замовчуванням і мікрофон за замовчуванням.
    private readonly StackPanel _widgetOutput = new();
    private readonly TextBlock _widgetOutputName = MixerUi.Text("", 14, semibold: true);
    private readonly TextBlock _widgetNoOutputs = MixerUi.Text(MixerStrings.NoOutputs, 13, secondary: true);
    private readonly Grid _widgetMic = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly MuteButton _widgetMicButton = new(isMic: true);
    private readonly TextBlock _widgetMicName = MixerUi.Text("", 13);
    private readonly TextBlock _widgetNoInputs = MixerUi.Text(MixerStrings.NoInputs, 13, secondary: true);
    private readonly VolumeRow? _widgetVolume;
    private string? _widgetOutputId;
    private string? _widgetMicId;

    private List<LevelMeter> _meters = [];
    private volatile AudioSnapshot? _latestSnapshot;
    private volatile PeakSample? _latestPeaks;
    private int _snapshotQueued;
    private int _peaksQueued;
    private IDisposable? _ticket;
    private bool _ticketHasMic;
    private bool _hadMics;
    private bool _attached;

    public MixerView(MixerHost host, MixerMode mode)
    {
        Host = host;
        Mode = mode;
        _ctx = new MixerContext(host, mode, Dispatcher, ScheduleCurrent);

        _unavailable.Visibility = Visibility.Collapsed;
        _unavailable.TextWrapping = TextWrapping.Wrap;
        _root.Children.Add(_unavailable);

        _compactButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _compactGlyph, _compactLabel },
        };
        _compactButton.Click += (_, _) => ToggleCompact();
        UpdateCompactButton();

        // Плитки лише в панелі біля годинника. Їх ставить у самий низ MixerFlyout, поза прокруткою.
        if (mode == MixerMode.Flyout) Tiles = new QuickTiles(_ctx);

        if (mode == MixerMode.Widget)
        {
            _widgetVolume = new VolumeRow(_ctx, isMic: false) { Margin = new Thickness(0, 6, 0, 0) };
            BuildWidget();
        }
        else
        {
            BuildSections();
        }

        Content = _root;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public MixerHost Host { get; }

    public MixerMode Mode { get; }

    /// <summary>
    /// Плитки «Звук» і «Мікрофони» внизу панелі біля годинника; в інших режимах — null. Елемент не входить у сам мікшер:
    /// панель ставить його під прокруткою, щоб плитки були видні завжди.
    /// </summary>
    internal QuickTiles? Tiles { get; }

    /// <summary>Картки пристроїв виводу в порядку на екрані.</summary>
    internal IReadOnlyList<DeviceCard> OutputCards => _outputPanel.Children.OfType<DeviceCard>().ToList();

    /// <summary>Картки мікрофонів у порядку на екрані.</summary>
    internal IReadOnlyList<DeviceCard> InputCards => _inputPanel.Children.OfType<DeviceCard>().ToList();

    internal bool IsUnavailableShown => _unavailable.Visibility == Visibility.Visible;

    internal string UnavailableText => _unavailable.Text;

    internal VolumeRow? WidgetVolume => _widgetVolume;

    internal MuteButton WidgetMicButton => _widgetMicButton;

    internal string WidgetOutputName => _widgetOutputName.Text;

    internal string WidgetMicName => _widgetMicName.Text;

    /// <summary>Мікшер видно: не сховано й (якщо він у вікні) вікно не сховане. Без вікна, наприклад у знімку, вважаємо видимим.</summary>
    private bool IsShown => Visibility == Visibility.Visible && (IsVisible || PresentationSource.FromVisual(this) is null);

    /// <summary>Підписатися на бекенд і налаштування, намалювати поточний знімок і за потреби почати вимірювати рівні.</summary>
    internal void Attach()
    {
        if (_attached) return;
        _attached = true;

        Host.Backend.Changed += OnBackendChanged;
        Host.Backend.Peaks += OnBackendPeaks;
        Host.Options.Changed += OnOptionChanged;
        IsVisibleChanged += OnIsVisibleChanged;

        Apply(Host.Backend.Current);
        UpdateMeterTicket();
    }

    /// <summary>Відписатися від усього й закрити «квиток» вимірювання рівнів.</summary>
    internal void Detach()
    {
        if (!_attached) return;
        _attached = false;

        Host.Backend.Changed -= OnBackendChanged;
        Host.Backend.Peaks -= OnBackendPeaks;
        Host.Options.Changed -= OnOptionChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        CloseTicket();
    }

    /// <summary>
    /// Узгодити «квиток» вимірювання з тим, чи мікшер видно й чи ввімкнено індикатори. Рівень мікрофона просимо
    /// лише там, де його індикатор справді на екрані: на сторінці й у панелі, коли є картка мікрофона. Віджет видно
    /// завжди, і мікрофон через нього не має бути відкритий постійно.
    /// </summary>
    internal void UpdateMeterTicket()
    {
        var want = _attached && Host.Options.ShowMeters && IsShown;
        var wantMic = want && Mode != MixerMode.Widget && _inputCards.Count > 0;
        if (!want)
        {
            if (_ticket is not null) CloseTicket();
            return;
        }

        if (_ticket is not null && _ticketHasMic == wantMic) return;

        try
        {
            // Новий квиток відкриваємо до закриття старого: бекенд не зупиняє вимірювання посеред перепросу.
            var next = Host.Backend.BeginMetering(microphone: wantMic);
            var previous = _ticket;
            _ticket = next;
            _ticketHasMic = wantMic;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _ctx.Report(Strings.OsdCommandFailed, ex);
        }
    }

    /// <summary>
    /// Синхронно намалювати поточний знімок бекенда, навіть якщо мікшер ще не показано й не підписано на події.
    /// Потрібно панелі біля годинника: розмір вікна міряється до показу, а схований мікшер знімків не малює.
    /// </summary>
    internal void RefreshNow() => Apply(Host.Backend.Current);

    /// <summary>Запланувати перемалювання за поточним знімком бекенда (можна з будь-якого потоку).</summary>
    internal void ScheduleCurrent()
    {
        _latestSnapshot = Host.Backend.Current;
        QueueSnapshot();
    }

    private void CloseTicket()
    {
        _ticket?.Dispose();
        _ticket = null;
        _ticketHasMic = false;
        foreach (var meter in _meters) meter.Reset();
    }

    private void OnBackendChanged(object? sender, AudioSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        QueueSnapshot();
    }

    private void OnBackendPeaks(object? sender, PeakSample sample)
    {
        _latestPeaks = sample;
        if (Interlocked.Exchange(ref _peaksQueued, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(FlushPeaks));
    }

    private void QueueSnapshot()
    {
        if (Interlocked.Exchange(ref _snapshotQueued, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(FlushSnapshot));
    }

    private void FlushSnapshot()
    {
        Volatile.Write(ref _snapshotQueued, 0);

        // Сховану панель не малюємо: коли її покажуть, IsVisibleChanged візьме свіжий знімок.
        if (!_attached || !IsShown) return;
        Apply(_latestSnapshot ?? Host.Backend.Current);
    }

    private void FlushPeaks()
    {
        Volatile.Write(ref _peaksQueued, 0);
        if (!_attached || _ticket is null || !Host.Options.ShowMeters || _latestPeaks is not { } sample) return;

        foreach (var meter in _meters) meter.SetLevel(sample.Get(meter.Key));
    }

    private void OnOptionChanged(object? sender, string key)
    {
        if (key is not (SettingKeys.ShowMeters or SettingKeys.SeparateCommunications or SettingKeys.PerAppOutput or SettingKeys.Compact)) return;

        // Подія може прийти з будь-якого потоку; змінюємо інтерфейс лише в його потоці.
        if (Dispatcher.CheckAccess()) HandleOptionChanged(key);
        else Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => HandleOptionChanged(key)));
    }

    private void HandleOptionChanged(string key)
    {
        if (!_attached) return;
        if (key == SettingKeys.ShowMeters) UpdateMeterTicket();
        ScheduleCurrent();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateMeterTicket();
        if (IsShown) ScheduleCurrent();
    }

    private void Apply(AudioSnapshot snapshot)
    {
        _ctx.Snapshot = snapshot;
        _unavailable.Visibility = Show(!snapshot.IsAvailable);

        if (Mode == MixerMode.Widget)
        {
            ApplyWidget(snapshot);
            return;
        }

        _outputSection.Visibility = Show(snapshot.IsAvailable);
        _inputSection.Visibility = Show(snapshot.IsAvailable);
        if (Tiles is not null)
        {
            Tiles.Visibility = Show(snapshot.IsAvailable);
            Tiles.Update(snapshot);
        }

        UpdateCompactButton();

        // Пристрій за замовчуванням іде першим, решта — як у знімку (OrderBy стабільний).
        var outputs = snapshot.Outputs.OrderBy(d => d.IsDefault ? 0 : 1).ToList();
        var inputs = snapshot.Inputs.OrderBy(d => d.IsDefault ? 0 : 1).ToList();
        PanelSync.Apply(_outputPanel, _outputCards, outputs, d => d.Id, d => new DeviceCard(_ctx, d), (card, d) => card.Update(d));
        PanelSync.Apply(_inputPanel, _inputCards, inputs, d => d.Id, d => new DeviceCard(_ctx, d), (card, d) => card.Update(d));
        _noOutputs.Visibility = Show(outputs.Count == 0);
        _noInputs.Visibility = Show(inputs.Count == 0);

        _meters = _outputCards.Values.Concat(_inputCards.Values).SelectMany(c => c.Meters).ToList();

        // Мікрофон з'явився чи зник: індикатор мікрофона або з'явився, або ні, тож квиток треба перевідкрити.
        var hasMics = _inputCards.Count > 0;
        if (hasMics == _hadMics) return;
        _hadMics = hasMics;
        UpdateMeterTicket();
    }

    private void ApplyWidget(AudioSnapshot snapshot)
    {
        var output = snapshot.DefaultOutput;
        var mic = snapshot.DefaultInput;

        _widgetOutput.Visibility = Show(snapshot.IsAvailable && output is not null);
        _widgetNoOutputs.Visibility = Show(snapshot.IsAvailable && output is null);
        _widgetMic.Visibility = Show(snapshot.IsAvailable && mic is not null);
        _widgetNoInputs.Visibility = Show(snapshot.IsAvailable && mic is null);

        _widgetOutputId = output?.Id;
        if (output is not null)
        {
            var name = string.IsNullOrWhiteSpace(output.Name) ? Strings.UnknownDevice : output.Name;
            _widgetOutputName.Text = name;
            _widgetOutputName.ToolTip = name;
            _widgetVolume!.Meter.Key = PeakKeys.Device(output.Id);
            _widgetVolume.Update(name, output.Volume, output.IsMuted);
        }

        _widgetMicId = mic?.Id;
        if (mic is not null)
        {
            var name = string.IsNullOrWhiteSpace(mic.Name) ? Strings.UnknownDevice : mic.Name;
            _widgetMicName.Text = name;
            _widgetMicName.ToolTip = name;
            _widgetMicButton.Update(name, 100, mic.IsMuted);
        }

        _meters = [_widgetVolume!.Meter];
    }

    private void BuildSections()
    {
        _root.Margin = new Thickness(Mode == MixerMode.Flyout ? 12 : 0);
        // Сталу ширину сторінки не визначає вміст: інакше згорнутий мікшер (самі назви) був би вужчим за розгорнутий
        // і кнопка «Згорнути все» після кліку тікала б з-під курсора. 580 вміщається в найменше вікно лаунчера (900 DIP).
        if (Mode == MixerMode.Page) HorizontalAlignment = HorizontalAlignment.Left;
        if (Mode == MixerMode.Page) Width = 580;

        if (Mode == MixerMode.Flyout) _root.Children.Add(CreateFlyoutHeader());
        else _root.Children.Add(CreatePageHeader());

        _noOutputs.Visibility = Visibility.Collapsed;
        _noInputs.Visibility = Visibility.Collapsed;
        BuildSection(_outputSection, MixerStrings.OutputSection, _outputPanel, _noOutputs);
        BuildSection(_inputSection, MixerStrings.InputSection, _inputPanel, _noInputs);
        _root.Children.Add(_outputSection);
        _root.Children.Add(_inputSection);

        if (Mode == MixerMode.Page) _root.Children.Add(CreateLinks());
    }

    private void BuildSection(StackPanel section, string title, StackPanel cards, TextBlock empty)
    {
        var header = MixerUi.Text(title, Mode == MixerMode.Flyout ? 14 : 16, semibold: true);
        header.Margin = new Thickness(0, Mode == MixerMode.Flyout ? 4 : 8, 0, 8);
        empty.Margin = new Thickness(0, 0, 0, 8);
        section.Children.Add(header);
        section.Children.Add(cards);
        section.Children.Add(empty);
    }

    private UIElement CreateFlyoutHeader()
    {
        var title = MixerUi.Text(MixerStrings.FlyoutTitle, 16, semibold: true);
        var settings = new SubtleButton
        {
            Width = 32,
            Height = 32,
            Content = MixerUi.Glyph(Glyphs.Settings, 16),
            ToolTip = MixerStrings.LinkSoundSettings,
        };
        System.Windows.Automation.AutomationProperties.SetName(settings, MixerStrings.LinkSoundSettings);
        settings.Click += (_, _) => CallShell(Host.Shell.OpenSoundSettings);

        _compactButton.Width = 32;
        _compactButton.Height = 32;
        _compactButton.Margin = new Thickness(0, 0, 4, 0);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_compactButton, 1);
        Grid.SetColumn(settings, 2);
        header.Children.Add(title);
        header.Children.Add(_compactButton);
        header.Children.Add(settings);
        return header;
    }

    // На сторінці модуля заголовок малює лаунчер, тож тут лише кнопка згортання праворуч.
    private UIElement CreatePageHeader()
    {
        _compactButton.Padding = new Thickness(8, 4, 8, 4);
        _compactButton.HorizontalAlignment = HorizontalAlignment.Right;
        _compactButton.Margin = new Thickness(0, 0, 0, 4);
        _compactLabel.Margin = new Thickness(6, 0, 0, 0);
        return _compactButton;
    }

    /// <summary>Кнопка показує, що станеться після кліку: «Згорнути все» або «Розгорнути все».</summary>
    private void UpdateCompactButton()
    {
        var compact = Host.Options.Compact;
        var text = compact ? MixerStrings.ExpandAll : MixerStrings.CollapseAll;
        _compactGlyph.Text = compact ? Glyphs.ChevronDown : Glyphs.ChevronUp;
        _compactLabel.Text = text;
        _compactLabel.Visibility = Show(Mode == MixerMode.Page);
        _compactButton.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(_compactButton, text);
    }

    private void ToggleCompact()
    {
        try
        {
            Host.Options.SetCompact(!Host.Options.Compact);
        }
        catch (Exception ex)
        {
            _ctx.Report(Strings.OsdCommandFailed, ex);
        }

        // Налаштування саме повідомить про зміну, але кнопка має відповісти одразу, навіть без підписки.
        UpdateCompactButton();
        ScheduleCurrent();
    }

    internal Button CompactButton => _compactButton;

    private UIElement CreateLinks()
    {
        var links = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AddLink(links, MixerStrings.LinkSoundSettings, Host.Shell.OpenSoundSettings);
        AddLink(links, MixerStrings.LinkAppVolume, Host.Shell.OpenAppVolumeSettings);
        AddLink(links, MixerStrings.LinkTaskbar, Host.Shell.OpenTaskbarSettings);
        return links;
    }

    private void AddLink(Panel panel, string text, Action open)
    {
        var link = new LinkButton(text);
        link.Click += (_, _) => CallShell(open);
        panel.Children.Add(link);
    }

    private void CallShell(Action open)
    {
        try
        {
            open();
        }
        catch (Exception ex)
        {
            _ctx.Report(Strings.OsdCommandFailed, ex);
        }
    }

    private void BuildWidget()
    {
        _root.Margin = new Thickness(12);
        _widgetVolume!.Slider.VolumeRequested = volume => _widgetOutputId is { } id
            ? _ctx.Run(() => _ctx.Backend.SetDeviceVolumeAsync(id, volume), Strings.OsdCommandFailed)
            : Task.CompletedTask;
        _widgetVolume.MuteRequested = muted => _widgetOutputId is { } id
            ? _ctx.Run(() => _ctx.Backend.SetDeviceMuteAsync(id, muted), Strings.OsdCommandFailed)
            : Task.CompletedTask;
        _widgetMicButton.Click += (_, _) =>
        {
            if (_widgetMicId is { } id)
                _ = _ctx.Run(() => _ctx.Backend.SetDeviceMuteAsync(id, !_widgetMicButton.IsMuted), Strings.OsdCommandFailed);
        };

        _widgetOutput.Children.Add(_widgetOutputName);
        _widgetOutput.Children.Add(_widgetVolume);

        _widgetMic.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _widgetMic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _widgetMicName.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_widgetMicName, 1);
        _widgetMic.Children.Add(_widgetMicButton);
        _widgetMic.Children.Add(_widgetMicName);

        foreach (var element in new UIElement[] { _widgetOutput, _widgetNoOutputs, _widgetMic, _widgetNoInputs })
        {
            element.Visibility = Visibility.Collapsed;
            _root.Children.Add(element);
        }

        _widgetNoInputs.Margin = new Thickness(0, 6, 0, 0);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
