using System.IO;
using Kastom.Modules.Audio.Actions;
using Kastom.Modules.Audio.Fake;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Shell;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Services;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Modules.Audio.Tests.Module;

/// <summary>Записує тексти підказок на екрані.</summary>
internal sealed class FakeNotifications : INotificationService
{
    public List<string> Texts { get; } = [];

    public void ShowOsd(string text) => Texts.Add(text);
}

internal sealed class FakeLogger : IModuleLogger
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<(string Message, Exception? Exception)> Errors { get; } = [];

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add((message, exception));
}

internal sealed class FakeForeground : IForegroundApp
{
    public ForegroundProcess? Current { get; set; }

    public ForegroundProcess? Get() => Current;
}

/// <summary>Виконує код одразу в поточному потоці.</summary>
internal sealed class ImmediateUi : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

internal sealed class TestActionRegistry : IActionRegistry
{
    private readonly List<ModuleAction> _actions = [];

    public IReadOnlyList<ModuleAction> Actions => _actions;

    public void Register(ModuleAction action) => _actions.Add(action);
}

internal sealed class TestWidgetRegistry : IWidgetRegistry
{
    private readonly List<WidgetDefinition> _widgets = [];

    public IReadOnlyList<WidgetDefinition> Widgets => _widgets;

    public void Register(WidgetDefinition widget) => _widgets.Add(widget);
}

internal sealed class TestModuleContext : IModuleContext
{
    public string ModuleId => "audio";

    public string DataDirectory => Path.GetTempPath();

    public FakeLogger Logger { get; } = new();

    public TestSettingsRegistry SettingsRegistry { get; } = new();

    public TestActionRegistry ActionRegistry { get; } = new();

    public TestWidgetRegistry WidgetRegistry { get; } = new();

    public FakeNotifications Osd { get; } = new();

    public IModuleLogger Log => Logger;

    public ISettingsRegistry Settings => SettingsRegistry;

    public IActionRegistry Actions => ActionRegistry;

    public IWidgetRegistry Widgets => WidgetRegistry;

    public INotificationService Notifications => Osd;

    public IUiDispatcher Ui { get; } = new ImmediateUi();
}

/// <summary>Дії на фальшивому бекенді з демо-даними: усе, що потрібно перевіряти в AudioActionsTests.</summary>
internal sealed class ActionsFixture
{
    private ActionsFixture()
    {
    }

    public FakeAudioBackend Backend { get; } = FakeAudioBackend.CreateDemo();

    public TestSettingsRegistry Settings { get; private set; } = null!;

    public AudioOptions Options { get; private set; } = null!;

    public FakeShell Shell { get; } = new();

    public FakeNotifications Osd { get; } = new();

    public FakeForeground Foreground { get; } = new();

    public AudioActions Actions { get; private set; } = null!;

    public int MixerOpened { get; private set; }

    public static async Task<ActionsFixture> CreateAsync(bool start = true)
    {
        var fixture = new ActionsFixture();
        (fixture.Settings, fixture.Options) = TestOptions.Create();
        var host = new MixerHost(fixture.Backend, fixture.Options, fixture.Shell);
        fixture.Actions = new AudioActions(host, fixture.Osd, fixture.Foreground, () => fixture.MixerOpened++);
        if (start) await fixture.Backend.StartAsync(CancellationToken.None);
        return fixture;
    }

    public AudioSnapshot Snapshot => Backend.Current;

    public AudioDevice Device(string nameStart) => Snapshot.Devices.First(d => d.Name.StartsWith(nameStart, StringComparison.Ordinal));

    public AudioApp App(string processName) => Snapshot.AllApps.First(a => a.ProcessName == processName);

    /// <summary>Виконати дію так, ніби натиснули клавішу.</summary>
    public Task Press(string actionId) => Actions.ExecuteAsync(actionId, ActionTrigger.Hotkey);

    public string LastOsd => Osd.Texts[^1];
}
