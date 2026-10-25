using Kastom.Sdk;
using Kastom.Sdk.Actions;
using Kastom.Sdk.Services;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Core.Runtime;

internal sealed class ModuleContext : IModuleContext
{
    public ModuleContext(
        string moduleId,
        KastomPaths paths,
        IModuleLogger log,
        INotificationService notifications,
        IUiDispatcher ui)
    {
        ModuleId = moduleId;
        DataDirectory = paths.ModuleDataDirectory(moduleId);
        Directory.CreateDirectory(DataDirectory);
        Log = log;
        Notifications = notifications;
        Ui = ui;
        SettingsRegistry = new ModuleSettingsRegistry(paths, moduleId);
    }

    public string ModuleId { get; }

    public string DataDirectory { get; }

    public IModuleLogger Log { get; }

    public INotificationService Notifications { get; }

    public IUiDispatcher Ui { get; }

    public ModuleSettingsRegistry SettingsRegistry { get; }

    public ModuleActionRegistry ActionRegistry { get; } = new();

    public ModuleWidgetRegistry WidgetRegistry { get; } = new();

    ISettingsRegistry IModuleContext.Settings => SettingsRegistry;

    IActionRegistry IModuleContext.Actions => ActionRegistry;

    IWidgetRegistry IModuleContext.Widgets => WidgetRegistry;
}
