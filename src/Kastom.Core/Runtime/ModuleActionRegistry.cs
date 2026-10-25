using Kastom.Sdk.Actions;

namespace Kastom.Core.Runtime;

internal sealed class ModuleActionRegistry : IActionRegistry
{
    private readonly object _sync = new();
    private readonly List<ModuleAction> _actions = [];

    public IReadOnlyList<ModuleAction> Actions
    {
        get
        {
            lock (_sync) return _actions.ToArray();
        }
    }

    public void Register(ModuleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IdPattern.IsValid(action.Id))
            throw new ArgumentException($"Неправильний ідентифікатор дії: '{action.Id}'.", nameof(action));

        lock (_sync)
        {
            if (_actions.Any(a => a.Id == action.Id))
                throw new ArgumentException($"Дія '{action.Id}' уже зареєстрована.", nameof(action));
            _actions.Add(action);
        }
    }
}
