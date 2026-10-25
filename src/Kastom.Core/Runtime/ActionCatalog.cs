using Kastom.Core.Abstractions;
using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Services;

namespace Kastom.Core.Runtime;

public sealed class ActionCatalog(IUiDispatcher ui, IModuleLogger log) : IActionCatalog
{
    private const string LauncherName = "Kastom";

    private readonly object _sync = new();
    private readonly List<ModuleAction> _launcherActions = [];
    private readonly List<ModuleActions> _modules = [];
    private IReadOnlyList<RegisteredAction> _all = [];

    public IReadOnlyList<RegisteredAction> All => Volatile.Read(ref _all);

    public event EventHandler? Changed;

    public RegisteredAction? Find(string globalId) => All.FirstOrDefault(a => a.GlobalId == globalId);

    public void RegisterLauncherAction(ModuleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ValidateId(action.Id);

        lock (_sync)
        {
            if (_launcherActions.Any(a => a.Id == action.Id))
                throw new ArgumentException($"Дія '{action.Id}' лаунчера вже зареєстрована.", nameof(action));

            _launcherActions.Add(action);
            Rebuild();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Замінює набір дій модуля (при запуску модуля).</summary>
    internal void SetModuleActions(string moduleId, string moduleName, IReadOnlyList<ModuleAction> moduleActions)
    {
        if (moduleId == KastomSdk.LauncherModuleId)
            throw new ArgumentException("Ідентифікатор модуля зарезервовано для лаунчера.", nameof(moduleId));

        var copy = moduleActions.ToArray();
        foreach (var action in copy) ValidateId(action.Id);
        var duplicate = copy.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Дія '{duplicate.Key}' модуля '{moduleId}' повторюється.", nameof(moduleActions));

        lock (_sync)
        {
            _modules.RemoveAll(m => m.ModuleId == moduleId);
            _modules.Add(new ModuleActions(moduleId, moduleName, copy));
            Rebuild();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Прибирає дії модуля (при зупинці). Якщо дій не було — нічого не робить.</summary>
    internal void RemoveModuleActions(string moduleId)
    {
        lock (_sync)
        {
            if (_modules.RemoveAll(m => m.ModuleId == moduleId) == 0) return;
            Rebuild();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ExecuteAsync(string globalId, ActionTrigger trigger)
    {
        var registered = Find(globalId);
        if (registered is null)
        {
            log.Warn($"Дію '{globalId}' не знайдено.");
            return;
        }

        try
        {
            Task? running = null;
            var context = new ActionContext { Trigger = trigger };
            await ui.InvokeAsync(() => running = registered.Action.Execute(context));
            if (running is not null) await running;
        }
        catch (Exception ex)
        {
            log.Error($"Дія '{globalId}' завершилась помилкою.", ex);
        }
    }

    // Спершу дії лаунчера, далі модулів у порядку запуску.
    private void Rebuild()
    {
        var all = _launcherActions
            .Select(a => new RegisteredAction(KastomSdk.LauncherModuleId, LauncherName, a))
            .Concat(_modules.SelectMany(m => m.Actions.Select(a => new RegisteredAction(m.ModuleId, m.ModuleName, a))))
            .ToArray();
        Volatile.Write(ref _all, all);
    }

    private static void ValidateId(string id)
    {
        if (!IdPattern.IsValid(id)) throw new ArgumentException($"Неправильний ідентифікатор дії: '{id}'.");
    }

    private sealed record ModuleActions(string ModuleId, string ModuleName, IReadOnlyList<ModuleAction> Actions);
}
