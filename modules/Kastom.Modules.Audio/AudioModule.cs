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
    private bool _fake;

    // Пишеться з фонового потоку (Start/Stop), читається в потоці інтерфейсу (дії, клік по значку).
    private volatile bool _running;
    private int _startGeneration;

    /// <summary>Спільний стан модуля; для тестів.</summary>
    internal MixerHost Host => _host!;

    public void Configure(IModuleContext context)
    {
        _context = context;
        AudioSettings.Define(context.Settings);

        IAudioBackend backend;
        if (Environment.GetEnvironmentVariable(FakeEnvironmentVariable) == "1")
        {
            context.Log.Info("Звук: фальшивий бекенд (KASTOM_AUDIO_FAKE=1), значків у треї немає.");
            backend = FakeAudioBackend.CreateDemo();
            _fake = true;
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
        var generation = Interlocked.Increment(ref _startGeneration);

        try
        {
            await host.Backend.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _running = true;

            // Фальшивий звук — для знімків і пісочниці: справжні значки біля годинника з вигаданим станом лише плутали б.
            if (!_fake)
            {
                await context.Ui.InvokeAsync(ShowTray).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            // Лаунчер кличе StopAsync лише після успішного старту: тайм-аут чи збій тут прибираємо самі.
            // Запуск, що довершився вже після тайм-ауту, коли модуль увімкнули знову, чужого не прибирає.
            if (generation == Volatile.Read(ref _startGeneration)) await AbortStartAsync(context, host).ConfigureAwait(false);
            else context.Log.Warn("Звук: запізнілий запуск завершився після нового — прибирати нічого.");
            throw;
        }

        context.Log.Info("Звук: модуль запущено.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var context = _context!;
        var host = _host!;

        _running = false;
        await ReleaseUiAsync(context).ConfigureAwait(false);

        await host.Backend.StopAsync().ConfigureAwait(false);
        context.Log.Info("Звук: модуль зупинено.");
    }

    // Лише в потоці інтерфейсу. Значки створюються один раз і живуть далі (див. AudioTrayIcons); повторний запуск їх лише показує.
    private void ShowTray()
    {
        try
        {
            _tray ??= new AudioTrayIcons(_host!, ToggleMixer, ShowMixer);
            _tray.Activate();
        }
        catch (Exception ex)
        {
            // Без значків мікшер і гарячі клавіші працюють, тож не валимо через них увесь модуль.
            _context!.Log.Error("Звук: не вдалося створити значки в треї, модуль працює без них.", ex);
            _tray?.Deactivate();
        }
    }

    private async Task AbortStartAsync(IModuleContext context, MixerHost host)
    {
        _running = false;
        await ReleaseUiAsync(context).ConfigureAwait(false);

        try
        {
            await host.Backend.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Первісну причину збою не затуляємо.
            context.Log.Warn($"Звук: не вдалося зупинити звук після збою запуску: {ex.Message}");
        }
    }

    private async Task ReleaseUiAsync(IModuleContext context)
    {
        try
        {
            await context.Ui.InvokeAsync(() =>
            {
                _tray?.Deactivate();
                _flyout?.Close();
                _flyout = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Лаунчер міг уже закрити потік інтерфейсу; бекенд усе одно треба зупинити.
            context.Log.Warn($"Звук: не вдалося прибрати значки: {ex.Message}");
        }
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
    internal MixerFlyout Flyout() => _flyout ??= new MixerFlyout(_host!);

    /// <summary>Панель, якщо вона вже створена; для тестів.</summary>
    internal MixerFlyout? CurrentFlyout => _flyout;

    // Дія з черги може дійти вже після StopAsync: без цього вона відкрила б нову панель при зупиненому звуку.
    private void ToggleMixer()
    {
        if (_running) Flyout().Toggle(CursorPosition());
    }

    private void ShowMixer()
    {
        if (_running) Flyout().ShowNear(CursorPosition());
    }

    private static Point CursorPosition()
    {
        var position = System.Windows.Forms.Cursor.Position;
        return new Point(position.X, position.Y);
    }
}
