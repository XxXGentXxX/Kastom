using Kastom.Sdk.Widgets;

namespace Kastom.Core.Runtime;

internal sealed class ModuleWidgetRegistry : IWidgetRegistry
{
    private readonly object _sync = new();
    private readonly List<WidgetDefinition> _widgets = [];

    public IReadOnlyList<WidgetDefinition> Widgets
    {
        get
        {
            lock (_sync) return _widgets.ToArray();
        }
    }

    public void Register(WidgetDefinition widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (!IdPattern.IsValid(widget.Id))
            throw new ArgumentException($"Неправильний ідентифікатор віджета: '{widget.Id}'.", nameof(widget));

        lock (_sync)
        {
            if (_widgets.Any(w => w.Id == widget.Id))
                throw new ArgumentException($"Віджет '{widget.Id}' уже зареєстровано.", nameof(widget));
            _widgets.Add(widget);
        }
    }
}
