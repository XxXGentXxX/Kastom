using System.Windows.Threading;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Microsoft.Win32;

namespace Kastom.Modules.Audio.Tray;

/// <summary>
/// Значки звуку (З11) і мікрофона (З12) в області сповіщень. Створюється й живе в потоці інтерфейсу.
/// <para>
/// Обидва NotifyIcon створюються один раз і лишаються тими самими на весь час життя модуля в процесі, завжди
/// в порядку «звук, потім мікрофон»: WinForms бере для кожного нового NotifyIcon новий uID, а Windows пам'ятає
/// «показувати біля годинника» саме за exe та uID. Перестворення скидало б закріплення й плодило б дублікати в
/// Параметрах. Тому налаштування, поява мікрофона й зупинка модуля лише перемикають <c>Visible</c>. Звільнити
/// NotifyIcon справді було б можна лише при вивантаженні модуля, а такої події в контракті немає.
/// </para>
/// </summary>
internal sealed class AudioTrayIcons
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly MixerHost _host;
    private readonly Action _toggleMixer;
    private readonly Action _showMixer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly EventHandler<AudioSnapshot> _onBackendChanged;
    private readonly UserPreferenceChangedEventHandler _onPreferenceChanged;
    private readonly Entry _sound;
    private readonly Entry _mic;
    private int _refreshQueued;
    private bool _active;

    /// <param name="toggleMixer">Лівий клік по значку звуку.</param>
    /// <param name="showMixer">Пункт «Мікшер» у меню.</param>
    public AudioTrayIcons(MixerHost host, Action toggleMixer, Action showMixer)
    {
        _host = host;
        _toggleMixer = toggleMixer;
        _showMixer = showMixer;
        _onBackendChanged = (_, _) => QueueRefresh();
        _onPreferenceChanged = (_, _) => QueueRefresh();

        // Порядок створення визначає порядок uID: звук завжди перший.
        _sound = CreateSoundEntry();
        _mic = CreateMicEntry();
    }

    /// <summary>Модуль запущено: стежити за звуком і темою та показати значки, які ввімкнено.</summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;

        _host.Backend.Changed += _onBackendChanged;
        // Статична подія: без відписки в Deactivate вона тримала б значки в пам'яті до кінця процесу.
        SystemEvents.UserPreferenceChanged += _onPreferenceChanged;
        ApplySettings();
    }

    /// <summary>Модуль зупинено: відписатися й прибрати значки з області сповіщень (самі NotifyIcon лишаються).</summary>
    public void Deactivate()
    {
        if (!_active) return;
        _active = false;

        _host.Backend.Changed -= _onBackendChanged;
        SystemEvents.UserPreferenceChanged -= _onPreferenceChanged;
        _sound.Hide();
        _mic.Hide();
    }

    /// <summary>Показати чи сховати значки за налаштуваннями й перемалювати ті, що змінилися.</summary>
    public void ApplySettings()
    {
        if (!_active) return;

        var snapshot = _host.Backend.Current;
        var light = IsLightTaskbar();
        var size = Math.Max(System.Windows.Forms.SystemInformation.SmallIconSize.Width, 16);

        _sound.Apply(TrayLogic.Sound(_host.Options.TrayIcon, snapshot.DefaultOutput), light, size);
        _mic.Apply(TrayLogic.Mic(_host.Options.MicTrayIcon, snapshot.DefaultInput), light, size);
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

    // Події бекенда приходять з чужого потоку, а часті зміни (повзунок) зливаються в одне перемальовування.
    private void QueueRefresh()
    {
        if (!_active || Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            ApplySettings();
        });
    }

    private void Guarded(Action action) => TrayLogic.RunGuarded(_host.Shell, action);

    private Entry CreateSoundEntry()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Strings.TrayMenuMixer, null, (_, _) => Guarded(_showMixer));
        menu.Items.Add(Strings.TrayMenuSoundSettings, null, (_, _) => Guarded(_host.Shell.OpenSoundSettings));
        menu.Items.Add(Strings.TrayMenuAppVolume, null, (_, _) => Guarded(_host.Shell.OpenAppVolumeSettings));
        return new Entry(menu, () => Guarded(_toggleMixer));
    }

    private Entry CreateMicEntry()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var toggle = new System.Windows.Forms.ToolStripMenuItem(Strings.TrayMenuMicMute);
        toggle.Click += (_, _) => ToggleMic();
        menu.Items.Add(toggle);
        menu.Items.Add(Strings.TrayMenuMixer, null, (_, _) => Guarded(_showMixer));
        menu.Items.Add(Strings.TrayMenuMicSettings, null, (_, _) => Guarded(_host.Shell.OpenInputProperties));
        menu.Opening += (_, _) => Guarded(() =>
        {
            var mic = _host.Backend.Current.DefaultInput;
            toggle.Enabled = mic is not null;
            toggle.Text = mic?.IsMuted == true ? Strings.TrayMenuMicUnmute : Strings.TrayMenuMicMute;
        });
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

    /// <summary>Один значок: NotifyIcon, меню й намальований Icon; перемальовує лише тоді, коли вигляд справді змінився.</summary>
    private sealed class Entry
    {
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private System.Drawing.Icon? _icon;
        private TrayLook? _look;
        private string? _text;

        public Entry(System.Windows.Forms.ContextMenuStrip menu, Action leftClick)
        {
            // Visible лишається false, поки Apply не покаже значок: тоді Windows ще нічого не бачила.
            _notifyIcon = new System.Windows.Forms.NotifyIcon { ContextMenuStrip = menu };
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) leftClick();
            };
        }

        public void Apply(TrayIconState state, bool light, int size)
        {
            if (!state.Visible)
            {
                Hide();
                return;
            }

            var look = new TrayLook(state.Glyph, light, size);
            if (TrayLogic.NeedsRedraw(_look, look))
            {
                var icon = TrayIconRenderer.Render(look.Glyph, look.Light, look.Size);
                _notifyIcon.Icon = icon;
                _icon?.Dispose();
                _icon = icon;
                _look = look;
            }

            if (TrayLogic.NeedsTooltip(_text, state.Tooltip))
            {
                _notifyIcon.Text = state.Tooltip;
                _text = state.Tooltip;
            }

            // Показуємо лише тепер: значок без намальованого Icon був би порожнім квадратом.
            if (!_notifyIcon.Visible) _notifyIcon.Visible = true;
        }

        public void Hide()
        {
            if (_notifyIcon.Visible) _notifyIcon.Visible = false;
        }
    }
}
