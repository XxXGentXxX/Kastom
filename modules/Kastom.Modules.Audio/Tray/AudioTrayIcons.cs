using System.Windows.Threading;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Microsoft.Win32;

namespace Kastom.Modules.Audio.Tray;

/// <summary>
/// Значки звуку (З11) і мікрофона (З12) в області сповіщень. Створюється й живе в потоці інтерфейсу.
/// Значок не існує, поки його вимкнено в налаштуваннях (а мікрофона — поки немає мікрофона за замовчуванням).
/// </summary>
internal sealed class AudioTrayIcons : IDisposable
{
    // Windows не приймає довшу підказку.
    private const int MaxTooltipLength = 127;
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly MixerHost _host;
    private readonly Action _toggleMixer;
    private readonly Action _showMixer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly EventHandler<AudioSnapshot> _onBackendChanged;
    private readonly UserPreferenceChangedEventHandler _onPreferenceChanged;
    private Entry? _sound;
    private Entry? _mic;
    private int _refreshQueued;
    private bool _disposed;

    /// <param name="toggleMixer">Лівий клік по значку звуку.</param>
    /// <param name="showMixer">Пункт «Мікшер» у меню.</param>
    public AudioTrayIcons(MixerHost host, Action toggleMixer, Action showMixer)
    {
        _host = host;
        _toggleMixer = toggleMixer;
        _showMixer = showMixer;
        _onBackendChanged = (_, _) => QueueRefresh();
        _onPreferenceChanged = (_, _) => QueueRefresh();
        host.Backend.Changed += _onBackendChanged;
        // Статична подія: без відписки в Dispose вона тримала б значки в пам'яті до кінця процесу.
        SystemEvents.UserPreferenceChanged += _onPreferenceChanged;
    }

    /// <summary>Показати чи сховати значки за налаштуваннями й перемалювати ті, що змінилися.</summary>
    public void ApplySettings()
    {
        if (_disposed) return;

        var snapshot = _host.Backend.Current;
        var light = IsLightTaskbar();
        var size = Math.Max(System.Windows.Forms.SystemInformation.SmallIconSize.Width, 16);

        if (_host.Options.TrayIcon)
        {
            _sound ??= CreateSoundEntry();
            var (glyph, text) = DescribeSound(snapshot.DefaultOutput);
            _sound.Update(glyph, text, light, size);
        }
        else
        {
            _sound = Remove(_sound);
        }

        if (_host.Options.MicTrayIcon && snapshot.DefaultInput is { } mic)
        {
            _mic ??= CreateMicEntry();
            var glyph = mic.IsMuted ? TrayGlyph.MicOff : TrayGlyph.Mic;
            var text = Strings.Format(mic.IsMuted ? Strings.TrayMicOffTooltipFormat : Strings.TrayMicOnTooltipFormat, mic.Name);
            _mic.Update(glyph, text, light, size);
        }
        else
        {
            _mic = Remove(_mic);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Backend.Changed -= _onBackendChanged;
        SystemEvents.UserPreferenceChanged -= _onPreferenceChanged;
        _sound = Remove(_sound);
        _mic = Remove(_mic);
    }

    /// <summary>Світла тема панелі задач. Немає значення в реєстрі — темна, як у чистій Windows 11.</summary>
    internal static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static (TrayGlyph Glyph, string Text) DescribeSound(AudioDevice? output)
    {
        if (output is null) return (TrayGlyph.Muted, Strings.TrayNoDevice);
        if (output.IsMuted) return (TrayGlyph.Muted, Strings.Format(Strings.TrayMutedTooltipFormat, output.Name));

        var percent = VolumeMath.ToPercent(output.Volume);
        var glyph = percent < 1 ? TrayGlyph.Volume0
            : percent < 33 ? TrayGlyph.Volume1
            : percent < 66 ? TrayGlyph.Volume2
            : TrayGlyph.Volume3;
        return (glyph, Strings.Format(Strings.TrayVolumeTooltipFormat, output.Name, percent));
    }

    // Події бекенда приходять з чужого потоку, а часті зміни (повзунок) зливаються в одне перемальовування.
    private void QueueRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            ApplySettings();
        });
    }

    private Entry CreateSoundEntry()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Strings.TrayMenuMixer, null, (_, _) => _showMixer());
        menu.Items.Add(Strings.TrayMenuSoundSettings, null, (_, _) => _host.Shell.OpenSoundSettings());
        menu.Items.Add(Strings.TrayMenuAppVolume, null, (_, _) => _host.Shell.OpenAppVolumeSettings());
        return new Entry(menu, _toggleMixer);
    }

    private Entry CreateMicEntry()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var toggle = new System.Windows.Forms.ToolStripMenuItem(Strings.TrayMenuMicMute);
        toggle.Click += (_, _) => ToggleMic();
        menu.Items.Add(toggle);
        menu.Items.Add(Strings.TrayMenuMixer, null, (_, _) => _showMixer());
        menu.Items.Add(Strings.TrayMenuMicSettings, null, (_, _) => _host.Shell.OpenInputProperties());
        menu.Opening += (_, _) =>
        {
            var mic = _host.Backend.Current.DefaultInput;
            toggle.Enabled = mic is not null;
            toggle.Text = mic?.IsMuted == true ? Strings.TrayMenuMicUnmute : Strings.TrayMenuMicMute;
        };
        return new Entry(menu, ToggleMic);
    }

    private async void ToggleMic()
    {
        try
        {
            if (_host.Backend.Current.DefaultInput is not { } mic) return;
            await _host.Backend.SetDeviceMuteAsync(mic.Id, !mic.IsMuted);
        }
        catch (Exception ex)
        {
            _host.Shell.ReportError(Strings.OsdCommandFailed, ex);
        }
    }

    private static Entry? Remove(Entry? entry)
    {
        entry?.Dispose();
        return null;
    }

    /// <summary>Один значок: NotifyIcon, меню й намальований Icon; перемальовує лише тоді, коли вигляд справді змінився.</summary>
    private sealed class Entry : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.ContextMenuStrip _menu;
        private System.Drawing.Icon? _icon;
        private (TrayGlyph Glyph, bool Light, int Size)? _look;
        private string? _text;

        public Entry(System.Windows.Forms.ContextMenuStrip menu, Action leftClick)
        {
            _menu = menu;
            _notifyIcon = new System.Windows.Forms.NotifyIcon { ContextMenuStrip = menu };
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) leftClick();
            };
        }

        public void Update(TrayGlyph glyph, string text, bool light, int size)
        {
            var look = (glyph, light, size);
            if (_look != look)
            {
                var icon = TrayIconRenderer.Render(glyph, light, size);
                _notifyIcon.Icon = icon;
                _icon?.Dispose();
                _icon = icon;
                _look = look;
            }

            var tooltip = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
            if (_text != tooltip)
            {
                _notifyIcon.Text = tooltip;
                _text = tooltip;
            }

            // Показуємо лише тепер: значок без намальованого Icon був би порожнім квадратом.
            if (!_notifyIcon.Visible) _notifyIcon.Visible = true;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon?.Dispose();
        }
    }
}
