using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Розгорнуті налаштування мікрофона: підсилення, прослуховування, перехід до вікна Windows. Значення
/// драйвера приходять із <see cref="AudioDevice.Details"/> лише після запиту, тож до того видно «Завантаження…».
/// </summary>
internal sealed class MicSettingsPanel : StackPanel
{
    private static readonly TimeSpan BoostDelay = TimeSpan.FromMilliseconds(200);

    private readonly MixerContext _ctx;
    private readonly string _micId;
    private readonly TextBlock _loading = MixerUi.Text(MixerStrings.MicLoading, 13, secondary: true);
    private readonly StackPanel _content = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _boostGroup = new();
    private readonly Slider _boostSlider = new() { IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _boostValue = MixerUi.Text("", 13);
    private readonly TextBlock _noBoost = MixerUi.Text(MixerStrings.MicBoostUnavailable, 13, secondary: true);
    private readonly StackPanel _listenGroup = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _listenCheck = new() { Content = MixerStrings.MicListen };
    private readonly ComboBox _listenTarget = new() { DisplayMemberPath = nameof(TargetChoice.Name), MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DispatcherTimer _boostTimer;
    private bool _programmatic;
    private bool _boostDragging;
    private int _boostInFlight;
    private string _targetsSignature = "";

    public MicSettingsPanel(MixerContext ctx, string micId)
    {
        _ctx = ctx;
        _micId = micId;
        Margin = new Thickness(0, 8, 0, 0);
        Visibility = Visibility.Collapsed;

        _boostTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = BoostDelay };
        _boostTimer.Tick += (_, _) => SendBoost();

        var boostLabel = MixerUi.Text(MixerStrings.MicBoost, 13);
        boostLabel.Margin = new Thickness(0, 0, 0, 2);
        _boostValue.HorizontalAlignment = HorizontalAlignment.Right;
        _boostValue.TextTrimming = TextTrimming.None;
        AutomationProperties.SetName(_boostSlider, MixerStrings.MicBoost);

        var boostRow = new Grid();
        boostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        boostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        Grid.SetColumn(_boostValue, 1);
        boostRow.Children.Add(_boostSlider);
        boostRow.Children.Add(_boostValue);
        _boostGroup.Children.Add(boostLabel);
        _boostGroup.Children.Add(boostRow);

        var through = MixerUi.Text(MixerStrings.MicListenThrough, 13);
        through.Margin = new Thickness(0, 8, 0, 2);
        _listenGroup.Children.Add(_listenCheck);
        _listenGroup.Children.Add(through);
        _listenGroup.Children.Add(_listenTarget);
        AutomationProperties.SetName(_listenTarget, MixerStrings.MicListenThrough);

        var windowsSettings = new LinkButton(MixerStrings.MicWindowsSettings) { Margin = new Thickness(-6, 8, 0, 0) };
        windowsSettings.Click += (_, _) =>
        {
            try
            {
                ctx.Host.Shell.OpenInputProperties();
            }
            catch (Exception ex)
            {
                ctx.Report(Strings.OsdCommandFailed, ex);
            }
        };

        _content.Children.Add(_boostGroup);
        _content.Children.Add(_noBoost);
        _content.Children.Add(_listenGroup);
        _content.Children.Add(windowsSettings);
        Children.Add(_loading);
        Children.Add(_content);

        _boostSlider.ValueChanged += (_, _) => OnBoostChanged();
        _boostSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _boostDragging = true));
        _boostSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _boostDragging = false;
            if (_boostTimer.IsEnabled) SendBoost();
        }));
        _listenCheck.Click += (_, _) => SendListen();
        _listenTarget.SelectionChanged += (_, _) => SendListen();
    }

    internal bool IsLoadingVisible => _loading.Visibility == Visibility.Visible;

    internal bool IsBoostVisible => _content.Visibility == Visibility.Visible && _boostGroup.Visibility == Visibility.Visible;

    internal bool IsNoBoostVisible => _content.Visibility == Visibility.Visible && _noBoost.Visibility == Visibility.Visible;

    internal bool IsListenVisible => _content.Visibility == Visibility.Visible && _listenGroup.Visibility == Visibility.Visible;

    internal Slider BoostSlider => _boostSlider;

    internal CheckBox ListenCheck => _listenCheck;

    internal ComboBox ListenTarget => _listenTarget;

    internal string BoostText => _boostValue.Text;

    public void Update(AudioDevice mic)
    {
        var details = mic.Details;
        _loading.Visibility = details is null ? Visibility.Visible : Visibility.Collapsed;
        _content.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
        if (details is null) return;

        _programmatic = true;
        try
        {
            UpdateBoost(details.Boost);
            UpdateListen(details);
        }
        finally
        {
            _programmatic = false;
        }
    }

    private void UpdateBoost(MicBoost? boost)
    {
        _boostGroup.Visibility = boost is null ? Visibility.Collapsed : Visibility.Visible;
        _noBoost.Visibility = boost is null ? Visibility.Visible : Visibility.Collapsed;
        if (boost is null) return;

        var step = boost.StepDb > 0 ? boost.StepDb : 1;
        _boostSlider.IsEnabled = boost.MaxDb > boost.MinDb;
        _boostSlider.Minimum = boost.MinDb;
        _boostSlider.Maximum = Math.Max(boost.MaxDb, boost.MinDb);
        _boostSlider.TickFrequency = step;
        _boostSlider.SmallChange = step;
        _boostSlider.LargeChange = step;

        // Поки користувач крутить повзунок, значення з драйвера його не перебиває.
        if (!(_boostDragging || _boostTimer.IsEnabled || _boostInFlight > 0)) _boostSlider.Value = boost.LevelDb;
        _boostValue.Text = MixerStrings.Format(MixerStrings.MicBoostValueFormat, _boostSlider.Value);
    }

    private void UpdateListen(MicDetails details)
    {
        _listenGroup.Visibility = details.ListenSupported ? Visibility.Visible : Visibility.Collapsed;
        if (!details.ListenSupported) return;

        var outputs = _ctx.Snapshot.Outputs.ToList();
        var signature = string.Join("|", outputs.Select(o => o.Id + "=" + o.Name));
        if (signature != _targetsSignature)
        {
            _targetsSignature = signature;
            _listenTarget.Items.Clear();
            _listenTarget.Items.Add(new TargetChoice(null, MixerStrings.MicListenDefaultOutput));
            foreach (var output in outputs) _listenTarget.Items.Add(new TargetChoice(output.Id, output.Name));
        }

        var selected = _listenTarget.Items.OfType<TargetChoice>().FirstOrDefault(c => c.Id == details.ListenTargetId);
        _listenTarget.SelectedItem = selected ?? _listenTarget.Items[0];
        _listenCheck.IsChecked = details.ListenEnabled;
    }

    private void OnBoostChanged()
    {
        _boostValue.Text = MixerStrings.Format(MixerStrings.MicBoostValueFormat, _boostSlider.Value);
        if (_programmatic) return;

        // Драйверові не потрібен кожен крок перетягування: чекаємо, поки користувач зупиниться.
        _boostTimer.Stop();
        _boostTimer.Start();
    }

    private void SendBoost()
    {
        _boostTimer.Stop();
        var level = (float)_boostSlider.Value;
        _ = SendBoostAsync(level);
    }

    private async Task SendBoostAsync(float level)
    {
        _boostInFlight++;
        try
        {
            await _ctx.Run(() => _ctx.Backend.SetBoostAsync(_micId, level), Strings.OsdCommandFailed);
        }
        finally
        {
            _boostInFlight--;
        }
    }

    private void SendListen()
    {
        if (_programmatic) return;

        var enabled = _listenCheck.IsChecked == true;
        var target = (_listenTarget.SelectedItem as TargetChoice)?.Id;
        _ = _ctx.Run(() => _ctx.Backend.SetListenAsync(_micId, enabled, target), Strings.OsdCommandFailed);
    }

    private sealed record TargetChoice(string? Id, string Name);
}
