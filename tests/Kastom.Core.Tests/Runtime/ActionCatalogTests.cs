using System.IO;
using Kastom.Core.Runtime;
using Kastom.Sdk;
using Kastom.Sdk.Actions;

namespace Kastom.Core.Tests.Runtime;

public sealed class ActionCatalogTests
{
    private readonly ListLogger _log = new();

    private static ModuleAction Action(string id, Func<ActionContext, Task>? execute = null) =>
        new() { Id = id, Title = id, Execute = execute ?? (_ => Task.CompletedTask) };

    private ActionCatalog Create() => new(new InlineUi(), _log);

    [Fact]
    public void LauncherActions_AreUnderKastomModule()
    {
        var catalog = Create();
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        catalog.RegisterLauncherAction(Action("open-window"));

        var registered = Assert.Single(catalog.All);
        Assert.Equal(KastomSdk.LauncherModuleId, registered.ModuleId);
        Assert.Equal("Kastom", registered.ModuleName);
        Assert.Equal("kastom/open-window", registered.GlobalId);
        Assert.Same(registered.Action, catalog.Find("kastom/open-window")!.Action);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void DuplicateAndInvalidIds_AreRejected()
    {
        var catalog = Create();
        catalog.RegisterLauncherAction(Action("a"));

        Assert.Throws<ArgumentException>(() => catalog.RegisterLauncherAction(Action("a")));
        Assert.Throws<ArgumentException>(() => catalog.RegisterLauncherAction(Action("Bad_Id")));
        Assert.Throws<ArgumentException>(() => catalog.SetModuleActions("demo", "Демо", [Action("x"), Action("x")]));
        Assert.Throws<ArgumentException>(() => catalog.SetModuleActions("demo", "Демо", [Action("X")]));
        Assert.Throws<ArgumentException>(() => catalog.SetModuleActions(KastomSdk.LauncherModuleId, "Kastom", [Action("x")]));
        Assert.Single(catalog.All);
    }

    [Fact]
    public void SetAndRemoveModuleActions_RaiseChanged()
    {
        var catalog = Create();
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        catalog.SetModuleActions("demo", "Демо", [Action("hello"), Action("count")]);
        Assert.Equal(["demo/hello", "demo/count"], catalog.All.Select(a => a.GlobalId));
        Assert.All(catalog.All, a => Assert.Equal("Демо", a.ModuleName));

        catalog.SetModuleActions("demo", "Демо", [Action("hello")]);
        Assert.Single(catalog.All);

        catalog.RemoveModuleActions("demo");
        Assert.Empty(catalog.All);
        catalog.RemoveModuleActions("demo");

        Assert.Equal(3, changed);
    }

    [Fact]
    public async Task Execute_RunsActionWithTrigger()
    {
        var catalog = Create();
        ActionTrigger? seen = null;
        catalog.RegisterLauncherAction(Action("go", context =>
        {
            seen = context.Trigger;
            return Task.CompletedTask;
        }));

        await catalog.ExecuteAsync("kastom/go", ActionTrigger.Hotkey);

        Assert.Equal(ActionTrigger.Hotkey, seen);
    }

    [Fact]
    public async Task Execute_AwaitsReturnedTask()
    {
        var catalog = Create();
        var done = false;
        catalog.RegisterLauncherAction(Action("slow", async _ =>
        {
            await Task.Delay(50);
            done = true;
        }));

        await catalog.ExecuteAsync("kastom/slow", ActionTrigger.Ui);

        Assert.True(done);
    }

    [Fact]
    public async Task Execute_SwallowsAndLogsExceptions()
    {
        var catalog = Create();
        catalog.RegisterLauncherAction(Action("sync-boom", _ => throw new InvalidOperationException("sync")));
        catalog.RegisterLauncherAction(Action("async-boom", async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async");
        }));

        await catalog.ExecuteAsync("kastom/sync-boom", ActionTrigger.Ui);
        await catalog.ExecuteAsync("kastom/async-boom", ActionTrigger.Ui);

        Assert.Equal(2, _log.Lines.Count(l => l.StartsWith("[ERROR]")));
    }

    [Fact]
    public async Task Execute_UnknownId_LogsWarning()
    {
        var catalog = Create();

        await catalog.ExecuteAsync("nope/nothing", ActionTrigger.Ui);

        Assert.Contains(_log.Lines, l => l.StartsWith("[WARN]") && l.Contains("nope/nothing"));
    }
}
