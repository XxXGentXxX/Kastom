using Kastom.Core.Abstractions;
using Kastom.Core.Models;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Services;

namespace Kastom.Core.Tests.Hotkeys;

/// <summary>Бекенд, що не чіпає Windows: «зайняті» поєднання задаються в Taken, реєстрації записуються.</summary>
internal sealed class FakeBackend : IHotkeyBackend
{
    public HashSet<HotkeyGesture> Taken { get; } = new();

    /// <summary>Зараз зареєстровані: id → поєднання.</summary>
    public Dictionary<int, HotkeyGesture> Registered { get; } = new();

    public List<(int Id, HotkeyGesture Gesture)> RegisterCalls { get; } = new();

    public List<int> UnregisterCalls { get; } = new();

    public event EventHandler<int>? Pressed;

    public bool TryRegister(int id, HotkeyGesture gesture)
    {
        RegisterCalls.Add((id, gesture));
        if (Taken.Contains(gesture) || Registered.ContainsValue(gesture)) return false;
        Registered[id] = gesture;
        return true;
    }

    public void Unregister(int id)
    {
        UnregisterCalls.Add(id);
        Registered.Remove(id);
    }

    public void Press(int id) => Pressed?.Invoke(this, id);

    public void Dispose()
    {
    }
}

internal sealed class FakeCatalog : IActionCatalog
{
    private readonly List<RegisteredAction> _all = new();

    public IReadOnlyList<RegisteredAction> All => _all.ToList();

    public List<(string GlobalId, ActionTrigger Trigger)> Executed { get; } = new();

    public bool ThrowOnExecute { get; set; }

    public event EventHandler? Changed;

    public void Add(RegisteredAction action) => _all.Add(action);

    public void Remove(string globalId) => _all.RemoveAll(a => a.GlobalId == globalId);

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public RegisteredAction? Find(string globalId) => _all.FirstOrDefault(a => a.GlobalId == globalId);

    public Task ExecuteAsync(string globalId, ActionTrigger trigger)
    {
        if (ThrowOnExecute) throw new InvalidOperationException("boom");
        Executed.Add((globalId, trigger));
        return Task.CompletedTask;
    }

    public void RegisterLauncherAction(ModuleAction action) => Add(new RegisteredAction("kastom", "Лаунчер", action));
}

internal sealed class FakeStateStore : ILauncherStateStore
{
    public LauncherState State { get; } = new();

    public int SaveCount { get; private set; }

    public bool ThrowOnSave { get; set; }

    public event EventHandler? Saved;

    public void Save()
    {
        if (ThrowOnSave) throw new System.IO.IOException("disk full");
        SaveCount++;
        Saved?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeLogger : IModuleLogger
{
    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public void Info(string message)
    {
    }

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}
