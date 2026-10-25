using Kastom.Core.Abstractions;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Hotkeys;
using Kastom.Sdk.Services;

namespace Kastom.Core.Hotkeys;

/// <summary>
/// Прив'язки клавіш до дій: стан береться зі <see cref="ILauncherStateStore"/>, поєднання реєструє
/// <see cref="IHotkeyBackend"/>. Усі зміни проходять через повну перебудову (Rebuild).
/// </summary>
public sealed class HotkeyManager : IHotkeyManager
{
    // Ідентифікатори RegisterHotKey для програм: 0x0000–0xBFFF; останній зарезервовано під пробні реєстрації.
    private const int ProbeId = 0xBFFF;
    private const int MaxActionId = ProbeId - 1;

    private readonly IActionCatalog _actions;
    private readonly ILauncherStateStore _state;
    private readonly IHotkeyBackend _backend;
    private readonly IModuleLogger _log;
    private readonly object _gate = new();

    // id, під яким поєднання зареєстровано в бекенді → повний id дії; лише для Active.
    private readonly Dictionary<int, string> _registered = new();

    // Один id на дію на весь час життя процесу, щоб id не плавали між перебудовами.
    private readonly Dictionary<string, int> _ids = new();
    private IReadOnlyList<HotkeyBindingInfo> _bindings = [];
    private int _nextId;
    private int _suspendCount;
    private bool _disposed;

    public HotkeyManager(IActionCatalog actions, ILauncherStateStore state, IHotkeyBackend backend, IModuleLogger log)
    {
        _actions = actions;
        _state = state;
        _backend = backend;
        _log = log;

        _actions.Changed += OnActionsChanged;
        _backend.Pressed += OnPressed;
        lock (_gate) RebuildAndPersist();
    }

    public IReadOnlyList<HotkeyBindingInfo> Bindings
    {
        get
        {
            lock (_gate) return _bindings;
        }
    }

    public bool Enabled
    {
        get => _state.State.HotkeysEnabled;
        set
        {
            lock (_gate)
            {
                var old = _state.State.HotkeysEnabled;
                if (old == value) return;
                var error = ApplyChange(() => _state.State.HotkeysEnabled = value, () => _state.State.HotkeysEnabled = old);
                if (error is not null) _log.Warn(error);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public HotkeyAssignResult Assign(string globalActionId, HotkeyGesture? gesture)
    {
        string? error;
        lock (_gate)
        {
            var target = _actions.Find(globalActionId);
            if (target is null) return new HotkeyAssignResult(false, HotkeyMessages.UnknownAction);

            if (gesture is { } g && Validate(target, g) is { } rejected) return rejected;

            var text = gesture?.ToString() ?? "";
            error = ApplyChange(() => _state.State.Hotkeys[globalActionId] = text);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return error is null ? new HotkeyAssignResult(true) : new HotkeyAssignResult(false, error);
    }

    public HotkeyAssignResult ResetToDefault(string globalActionId)
    {
        HotkeyAssignResult result;
        lock (_gate)
        {
            var target = _actions.Find(globalActionId);
            if (target is null) return new HotkeyAssignResult(false, HotkeyMessages.UnknownAction);

            // Rebuild сам знову видасть типове поєднання (і збереже його), якщо воно вільне.
            var error = ApplyChange(() => _state.State.Hotkeys.Remove(globalActionId));
            result = error is null ? DescribeReset(target) : new HotkeyAssignResult(false, error);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public IDisposable Suspend()
    {
        lock (_gate)
        {
            if (_disposed) return new SuspendToken(null);
            if (++_suspendCount == 1) UnregisterAll();
            return new SuspendToken(this);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _actions.Changed -= OnActionsChanged;
            _backend.Pressed -= OnPressed;
            UnregisterAll();
            _bindings = [];
        }
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _suspendCount == 0) return;
            if (--_suspendCount > 0) return;
            RebuildAndPersist();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SuspendToken(HotkeyManager? owner) : IDisposable
    {
        private HotkeyManager? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Resume();
    }

    /// <summary>
    /// Змінити стан, перебудувати й записати на диск. Якщо запис не вдався — повернути все як було
    /// (стан у пам'яті й реєстрації) і віддати текст помилки. Викликати під <see cref="_gate"/>.
    /// </summary>
    private string? ApplyChange(Action change, Action? revert = null)
    {
        var snapshot = new Dictionary<string, string>(_state.State.Hotkeys);
        try
        {
            change();
            Rebuild();
            _state.Save();
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(HotkeyMessages.Get("LogSaveFailed"), ex);
            revert?.Invoke();
            _state.State.Hotkeys.Clear();
            foreach (var (key, value) in snapshot) _state.State.Hotkeys[key] = value;
            Rebuild();
            return ex.Message;
        }
    }

    /// <summary>Перебудова з записом типових поєднань, видних уперше (старт, зміна набору дій, кінець Suspend).</summary>
    private void RebuildAndPersist()
    {
        if (!Rebuild()) return;
        try
        {
            _state.Save();
        }
        catch (Exception ex)
        {
            _log.Error(HotkeyMessages.Get("LogSaveFailed"), ex);
        }
    }

    /// <summary>Перевірки перед призначенням; null — поєднання можна брати. Нічого не зберігає.</summary>
    private HotkeyAssignResult? Validate(RegisteredAction target, HotkeyGesture gesture)
    {
        if (!gesture.IsValid) return new HotkeyAssignResult(false, HotkeyMessages.InvalidGesture);
        if (KeyNames.RequiresModifier(gesture.VirtualKey) && gesture.Modifiers == HotkeyModifiers.None)
            return new HotkeyAssignResult(false, HotkeyMessages.NeedModifier);

        var targetId = target.GlobalId;
        foreach (var other in _bindings)
        {
            if (other.Action.GlobalId != targetId && other.Gesture == gesture) return Conflict(other.Action);
        }

        // Явно збережене поєднання теж рахується, навіть якщо його не вдалося зареєструвати.
        foreach (var (id, text) in _state.State.Hotkeys)
        {
            if (id == targetId || !HotkeyGesture.TryParse(text, out var stored) || stored != gesture) continue;
            if (_actions.Find(id) is { } owner) return Conflict(owner);
        }

        if (!Enabled) return null;

        var current = _bindings.FirstOrDefault(b => b.Action.GlobalId == targetId);
        if (current is { Status: HotkeyBindingStatus.Active } && current.Gesture == gesture && _suspendCount == 0) return null;

        // Пробна реєстрація: чи віддасть Windows це поєднання.
        var free = TryRegister(ProbeId, gesture);
        if (free) _backend.Unregister(ProbeId);
        return free ? null : new HotkeyAssignResult(false, HotkeyMessages.TakenBySystem);
    }

    private static HotkeyAssignResult Conflict(RegisteredAction owner) =>
        new(false, HotkeyMessages.Format("ConflictWithAction", owner.Action.Title, owner.ModuleName), owner.GlobalId);

    private HotkeyAssignResult DescribeReset(RegisteredAction target)
    {
        var binding = _bindings.First(b => b.Action.GlobalId == target.GlobalId);
        var defaultText = target.Action.DefaultHotkey?.ToString() ?? "";
        switch (binding.Status)
        {
            case HotkeyBindingStatus.Unassigned:
                if (target.Action.DefaultHotkey is not { } wanted)
                    return new HotkeyAssignResult(false, HotkeyMessages.ResetNoDefault);
                var holder = _bindings.FirstOrDefault(b => b.Action.GlobalId != target.GlobalId && b.Gesture == wanted);
                return holder is null
                    ? new HotkeyAssignResult(false, HotkeyMessages.Format("ResetNotApplied", defaultText))
                    : new HotkeyAssignResult(
                        false,
                        HotkeyMessages.Format("ResetConflict", defaultText, holder.Action.Action.Title, holder.Action.ModuleName),
                        holder.Action.GlobalId);
            case HotkeyBindingStatus.ConflictInternal:
                var owner = _bindings.FirstOrDefault(b => b.Gesture == binding.Gesture && b.Status == HotkeyBindingStatus.Active);
                return new HotkeyAssignResult(
                    false,
                    owner is null
                        ? HotkeyMessages.Format("ResetNotApplied", defaultText)
                        : HotkeyMessages.Format("ResetConflict", defaultText, owner.Action.Action.Title, owner.Action.ModuleName),
                    owner?.Action.GlobalId);
            case HotkeyBindingStatus.TakenBySystem:
                return new HotkeyAssignResult(false, HotkeyMessages.Format("ResetTakenBySystem", defaultText));
            default:
                return new HotkeyAssignResult(true);
        }
    }

    /// <summary>
    /// Перебудувати всі прив'язки й реєстрації. Типові поєднання, видані вперше, записуються в стан
    /// (на диск — після виклику). Повертає true, якщо стан змінився. Викликати під <see cref="_gate"/>.
    /// </summary>
    private bool Rebuild()
    {
        if (_disposed) return false;
        UnregisterAll();

        var ordered = _actions.All
            .Select((action, index) => (action, index))
            .OrderBy(x => x.action.ModuleId == KastomSdk.LauncherModuleId ? 0 : 1)
            .ThenBy(x => x.action.ModuleName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.action.ModuleId, StringComparer.Ordinal)
            .ThenBy(x => x.index)
            .Select(x => x.action)
            .ToList();

        // Крок 1: поєднання, які користувач задав явно (або навмисно прибрав), і вже видані типові.
        var gestures = new Dictionary<string, HotkeyGesture?>();
        foreach (var action in ordered)
        {
            if (!_state.State.Hotkeys.TryGetValue(action.GlobalId, out var text)) continue;
            if (text.Length == 0)
            {
                gestures[action.GlobalId] = null;
            }
            else if (HotkeyGesture.TryParse(text, out var stored) && IsUsable(stored))
            {
                gestures[action.GlobalId] = stored;
            }
            else
            {
                _log.Warn(HotkeyMessages.Format("LogUnusableStored", text, action.GlobalId));
                gestures[action.GlobalId] = null;
            }
        }

        // Крок 2: типові поєднання — лише там, де вони вільні. Видане типове одразу стає власністю дії
        // (записується в стан), тож пізніший модуль з таким самим типовим уже нічого не забере.
        var changed = false;
        var used = gestures.Values.Where(g => g is not null).Select(g => g!.Value).ToHashSet();
        foreach (var action in ordered)
        {
            if (gestures.ContainsKey(action.GlobalId)) continue;
            var fallback = action.Action.DefaultHotkey;
            if (fallback is { } d && IsUsable(d) && used.Add(d))
            {
                gestures[action.GlobalId] = d;
                _state.State.Hotkeys[action.GlobalId] = d.ToString();
                changed = true;
            }
            else
            {
                gestures[action.GlobalId] = null;
            }
        }

        // Крок 3: статуси й реєстрація в Windows.
        var bindings = new List<HotkeyBindingInfo>(ordered.Count);
        var active = new HashSet<HotkeyGesture>();
        foreach (var action in ordered)
        {
            if (gestures[action.GlobalId] is not { } gesture)
            {
                bindings.Add(new HotkeyBindingInfo(action, null, HotkeyBindingStatus.Unassigned));
                continue;
            }

            HotkeyBindingStatus status;
            if (!Enabled)
            {
                status = HotkeyBindingStatus.Disabled;
            }
            else if (active.Contains(gesture))
            {
                status = HotkeyBindingStatus.ConflictInternal;
            }
            else if (_suspendCount > 0)
            {
                // Реєстрація призупинена: показуємо, як було б після відновлення.
                status = HotkeyBindingStatus.Active;
                active.Add(gesture);
            }
            else if (IdFor(action.GlobalId) is { } id && TryRegister(id, gesture))
            {
                status = HotkeyBindingStatus.Active;
                active.Add(gesture);
                _registered[id] = action.GlobalId;
            }
            else
            {
                status = HotkeyBindingStatus.TakenBySystem;
            }

            bindings.Add(new HotkeyBindingInfo(action, gesture, status));
        }

        _bindings = bindings;
        return changed;
    }

    private int? IdFor(string globalId)
    {
        if (_ids.TryGetValue(globalId, out var id)) return id;
        if (_nextId >= MaxActionId)
        {
            _log.Error($"Закінчилися ідентифікатори гарячих клавіш; дію {globalId} не зареєстровано.");
            return null;
        }

        _ids[globalId] = ++_nextId;
        return _nextId;
    }

    private static bool IsUsable(HotkeyGesture gesture) =>
        gesture.IsValid && !(KeyNames.RequiresModifier(gesture.VirtualKey) && gesture.Modifiers == HotkeyModifiers.None);

    private bool TryRegister(int id, HotkeyGesture gesture)
    {
        try
        {
            return _backend.TryRegister(id, gesture);
        }
        catch (Exception ex)
        {
            _log.Error($"RegisterHotKey({gesture}): {ex.Message}", ex);
            return false;
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            try
            {
                _backend.Unregister(id);
            }
            catch (Exception ex)
            {
                _log.Error($"UnregisterHotKey({id}): {ex.Message}", ex);
            }
        }

        _registered.Clear();
    }

    private void OnActionsChanged(object? sender, EventArgs e)
    {
        lock (_gate) RebuildAndPersist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPressed(object? sender, int id)
    {
        string? globalId;
        lock (_gate)
        {
            if (!_registered.TryGetValue(id, out globalId)) return;
        }

        _ = ExecuteAsync(globalId);
    }

    private async Task ExecuteAsync(string globalId)
    {
        try
        {
            await _actions.ExecuteAsync(globalId, ActionTrigger.Hotkey);
        }
        catch (Exception ex)
        {
            _log.Error(HotkeyMessages.Format("LogExecuteFailed", globalId), ex);
        }
    }
}
