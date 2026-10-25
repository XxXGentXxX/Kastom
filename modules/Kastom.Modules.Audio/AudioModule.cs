using System.Windows;
using Kastom.Modules.Audio.Actions;
using Kastom.Modules.Audio.CoreAudio;
using Kastom.Modules.Audio.Fake;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Shell;
using Kastom.Modules.Audio.Tray;
using Kastom.Modules.Audio.Ui;
using Kastom.Sdk;
using Kastom.Sdk.Widgets;

namespace Kastom.Modules.Audio;

/// <summary>Модуль «Звук»: мікшер, значки в області сповіщень і дії для гарячих клавіш.</summary>
public sealed class AudioModule : IKastomModule
{
    /// <summary>Якщо дорівнює "1", замість справжнього звуку береться фальшивий бекенд (знімки сторінок, тести).</summary>
    internal const string FakeEnvironmentVariable = "KASTOM_AUDIO_FAKE";

    private IModuleContext? _context;
    private MixerHost? _host;
    private AudioTrayIcons? _tray;
    private MixerFlyout? _flyout;

    /// <summary>Спільний стан модуля; для тестів.</summary>
    internal MixerHost Host => _host!;

    public void Configure(IModuleContext context)
    {
        _context = context;
        AudioSettings.Define(context.Settings);

        IAudioBackend backend;
        if (Environment.GetEnvironmentVariable(FakeEnvironmentVariable) == "1")
        {
            context.Log.Info("Звук: фальшивий бекенд (KASTOM_AUDIO_FAKE=1).");
            backend = FakeAudioBackend.CreateDemo();
        }
        else
        {
            backend = new CoreAudioBackend(context.Log);
        }

        var host = new MixerHost(backend, new AudioOptions(context.Settings), new AudioShell(context.Log, context.Notifications));
        _host = host;

        new AudioActions(host, context.Notifications, new ForegroundApp(), ShowMixer).Register(context.Actions);

        context.Settings.SetCustomPage(() => new MixerView(host, MixerMode.Page));
        context.Widgets.Register(new WidgetDefinition
        {
            Id = "quick",
            Title = Strings.WidgetTitle,
            DefaultWidth = 320,
            DefaultHeight = 170,
            CreateContent = () => new MixerView(host, MixerMode.Widget),
        });

        host.Options.Changed += OnOptionChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var context = _context!;
        var host = _host!;

        await host.Backend.StartAsync(cancellationToken).ConfigureAwait(false);
        await context.Ui.InvokeAsync(() =>
        {
            _tray?.Dispose();
            _tray = new AudioTrayIcons(host, ToggleMixer, ShowMixer);
            _tray.ApplySettings();
        }).ConfigureAwait(false);
        context.Log.Info("Звук: модуль запущено.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var context = _context!;
        var host = _host!;

        try
        {
            await context.Ui.InvokeAsync(() =>
            {
                _tray?.Dispose();
                _tray = null;
                _flyout?.Close();
                _flyout = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Лаунчер міг уже закрити потік інтерфейсу; бекенд усе одно треба зупинити.
            context.Log.Warn($"Звук: не вдалося прибрати значки: {ex.Message}");
        }

        await host.Backend.StopAsync().ConfigureAwait(false);
        context.Log.Info("Звук: модуль зупинено.");
    }

    private void OnOptionChanged(object? sender, string key)
    {
        if (key is not (SettingKeys.TrayIcon or SettingKeys.MicTrayIcon)) return;

        // Налаштування можуть змінитися з будь-якого потоку, а значки живуть у потоці інтерфейсу.
        _ = _context!.Ui.InvokeAsync(() =>
        {
            try
            {
                _tray?.ApplySettings();
            }
            catch (Exception ex)
            {
                _context.Log.Error("Звук: не вдалося оновити значки.", ex);
            }
        });
    }

    // Лише в потоці інтерфейсу: панель — вікно WPF, одне на запуск модуля.
    private MixerFlyout Flyout() => _flyout ??= new MixerFlyout(_host!);

    private void ToggleMixer() => Flyout().Toggle(CursorPosition());

    private void ShowMixer() => Flyout().ShowNear(CursorPosition());

    private static Point CursorPosition()
    {
        var position = System.Windows.Forms.Cursor.Position;
        return new Point(position.X, position.Y);
    }
}
