using Kastom.Modules.Audio.Fake;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Ui;
using Kastom.Sdk.Actions;

namespace Kastom.Modules.Audio.Tests.Module;

public sealed class AudioModuleTests
{
    /// <summary>Модуль на фальшивому бекенді, зі значком, вимкненим в налаштуваннях (справжніх NotifyIcon у тестах немає).</summary>
    private static (AudioModule Module, TestModuleContext Context) Configured()
    {
        var context = new TestModuleContext();
        var module = new AudioModule();
        var previous = Environment.GetEnvironmentVariable(AudioModule.FakeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AudioModule.FakeEnvironmentVariable, "1");
        try
        {
            module.Configure(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AudioModule.FakeEnvironmentVariable, previous);
        }

        context.SettingsRegistry.Set(SettingKeys.TrayIcon, false);
        return (module, context);
    }

    [Fact]
    public void ConfigureRegistersSettingsActionsWidgetAndPage() => Sta.Run(() =>
    {
        var (module, context) = Configured();

        Assert.Contains(context.SettingsRegistry.Definitions, d => d.Key == SettingKeys.TrayIcon);
        Assert.Contains(context.SettingsRegistry.Definitions, d => d.Key == SettingKeys.MicSlot(1));
        Assert.Contains(context.ActionRegistry.Actions, a => a.Id == ActionIds.ToggleMic);
        Assert.Contains(context.ActionRegistry.Actions, a => a.Id == ActionIds.AppSlotDown(ActionIds.AppSlotCount));

        var widget = Assert.Single(context.WidgetRegistry.Widgets);
        Assert.Equal("quick", widget.Id);
        Assert.Equal(Strings.WidgetTitle, widget.Title);
        Assert.Equal(320, widget.DefaultWidth);
        Assert.Equal(170, widget.DefaultHeight);
        Assert.IsType<MixerView>(widget.CreateContent());

        Assert.IsType<MixerView>(context.SettingsRegistry.CustomPageFactory!());

        Assert.IsType<FakeAudioBackend>(module.Host.Backend);
        Assert.Contains(context.Logger.Infos, text => text.Contains("KASTOM_AUDIO_FAKE"));
        Assert.Empty(context.Logger.Errors);
    });

    [Fact]
    public void ConfigureDoesNotStartTheBackend() => Sta.Run(() =>
    {
        var (module, _) = Configured();

        Assert.False(((FakeAudioBackend)module.Host.Backend).IsStarted);
    });

    [Fact]
    public void StartStopStartWorksAndBackendFollows() => Sta.Run(() =>
    {
        var (module, context) = Configured();
        var backend = (FakeAudioBackend)module.Host.Backend;

        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(backend.IsStarted);
        Assert.True(backend.Current.IsAvailable);

        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(backend.IsStarted);

        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(backend.IsStarted);
        Assert.NotNull(backend.Current.DefaultOutput);

        // Налаштування значка змінюються й тоді, коли значка ще немає; нових вікон це не створює.
        context.SettingsRegistry.Set(SettingKeys.TrayIcon, false);
        context.SettingsRegistry.Set(SettingKeys.TrayMicBadge, false);
        context.SettingsRegistry.Set(SettingKeys.Osd, false);

        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(backend.IsStarted);
        Assert.Empty(context.Logger.Errors);
    });

    [Fact]
    public void CancelledStartStopsTheBackendAndThrows() => Sta.Run(() =>
    {
        var (module, context) = Configured();
        var backend = (FakeAudioBackend)module.Host.Backend;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var start = module.StartAsync(cancellation.Token);

        // Скасування виходить як OperationCanceledException: задача в стані Canceled.
        Assert.True(start.IsCanceled);
        Assert.False(backend.IsStarted);
        Assert.False(backend.Current.IsAvailable);
        Assert.Empty(context.Logger.Errors);

        // Недозапущений модуль не відкриває панель, але повторний запуск після цього працює.
        context.ActionRegistry.Actions.Single(a => a.Id == ActionIds.OpenMixer)
            .Execute(new ActionContext { Trigger = ActionTrigger.Hotkey }).GetAwaiter().GetResult();
        Assert.Null(module.CurrentFlyout);

        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(backend.IsStarted);
        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    [Fact]
    public void BackendFailureOnStartIsPassedOnAfterCleanup() => Sta.Run(() =>
    {
        var (module, context) = Configured();
        var backend = (FakeAudioBackend)module.Host.Backend;
        backend.Changed += (_, _) => throw new InvalidOperationException("підписник упав");

        var start = module.StartAsync(CancellationToken.None);

        Assert.IsType<InvalidOperationException>(Assert.Single(start.Exception!.InnerExceptions));
        Assert.False(backend.IsStarted);
        Assert.Empty(context.Logger.Errors);
    });

    [Fact]
    public void OpeningTheMixerAfterStopCreatesNoPanel() => Sta.Run(() =>
    {
        var (module, context) = Configured();
        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Дія з черги, що дійшла вже після зупинки.
        context.ActionRegistry.Actions.Single(a => a.Id == ActionIds.OpenMixer)
            .Execute(new ActionContext { Trigger = ActionTrigger.Hotkey }).GetAwaiter().GetResult();

        Assert.Null(module.CurrentFlyout);
    });

    [Fact]
    public void StopClosesThePanelAndRestartGetsANewOne() => Sta.Run(() =>
    {
        var (module, _) = Configured();
        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var first = module.Flyout();
        var closed = false;
        first.Closed += (_, _) => closed = true;
        Assert.Same(first, module.Flyout());

        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(closed);
        Assert.Null(module.CurrentFlyout);

        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var second = module.Flyout();
        Assert.NotSame(first, second);
        Assert.False(second.IsVisible);
        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    [Fact]
    public void StopWithoutStartIsHarmless() => Sta.Run(() =>
    {
        var (module, _) = Configured();

        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(((FakeAudioBackend)module.Host.Backend).IsStarted);
    });

    [Fact]
    public void RegisteredActionsWorkAfterStart() => Sta.Run(() =>
    {
        var (module, context) = Configured();
        module.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        context.ActionRegistry.Actions.Single(a => a.Id == ActionIds.ToggleMic)
            .Execute(new ActionContext { Trigger = ActionTrigger.Hotkey }).GetAwaiter().GetResult();

        Assert.True(module.Host.Backend.Current.DefaultInput!.IsMuted);
        Assert.Equal(Strings.OsdMicMuted, Assert.Single(context.Osd.Texts));
        module.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    });
}
