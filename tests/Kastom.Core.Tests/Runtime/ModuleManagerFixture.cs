using System.IO;
using System.Reflection;
using Kastom.Core.Abstractions;
using Kastom.Core.Installation;
using Kastom.Core.Models;
using Kastom.Core.Runtime;

namespace Kastom.Core.Tests.Runtime;

/// <summary>Менеджер модулів з підробленими каталогом і установником та реальною збіркою демо-модуля.</summary>
internal sealed class ModuleManagerFixture : IDisposable
{
    public ModuleManagerFixture()
    {
        Temp = new TempDir();
        Paths = Temp.Paths;
        Store = new JsonLauncherStateStore(Paths);
        Installer = new FakeInstaller(Paths, DemoOutput);
        Manager = CreateManager(Store);
    }

    public static string DemoOutput =>
        typeof(ModuleManagerFixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "DemoModuleOutput").Value!;

    public TempDir Temp { get; }

    public KastomPaths Paths { get; }

    public JsonLauncherStateStore Store { get; }

    public FakeCatalogService Catalog { get; } = new();

    public FakeInstaller Installer { get; }

    public FakeNotifications Notifications { get; } = new();

    public ListLogger Log { get; } = new();

    public InlineUi Ui { get; } = new();

    public ActionCatalog Actions => _actions ??= new ActionCatalog(Ui, Log);

    public ModuleManager Manager { get; }

    private ActionCatalog? _actions;

    /// <summary>Ще один менеджер над тим самим станом: «наступний запуск лаунчера».</summary>
    public ModuleManager CreateManager(JsonLauncherStateStore store) =>
        new(Paths, store, Catalog, Installer, Actions, Notifications, Ui, _ => Log, new ModuleVersion(0, 1, 0))
        {
            CompatibilityCheck = (_, _, _) => new CompatibilityResult(true),
        };

    /// <summary>Кладе зібраний демо-модуль у папку встановлених і записує його в стан.</summary>
    public void AddDemo(string version = "1.0.0", bool enabled = true, string? previousVersion = null, string? runMode = null)
    {
        var directory = Paths.ModuleInstallDirectory("demo", version);
        ModuleFiles.Copy(DemoOutput, directory);
        ModuleFiles.RewriteManifest(directory, "demo", "Демо-модуль", version, runMode: runMode);
        Register("demo", version, enabled, previousVersion);
    }

    /// <summary>
    /// Модуль із типом із самих тестів (<see cref="ThrowingModule"/>, <see cref="HangingModule"/>):
    /// збірка тестів копіюється в папку модуля.
    /// </summary>
    public void AddTestAssemblyModule(string id, string name, Type moduleType)
    {
        var source = Path.GetDirectoryName(moduleType.Assembly.Location)!;
        var directory = Paths.ModuleInstallDirectory(id, "1.0.0");
        Directory.CreateDirectory(directory);
        var assemblyName = Path.GetFileName(moduleType.Assembly.Location);
        File.Copy(Path.Combine(source, assemblyName), Path.Combine(directory, assemblyName), overwrite: true);
        var deps = Path.Combine(source, Path.ChangeExtension(assemblyName, ".deps.json"));
        if (File.Exists(deps)) File.Copy(deps, Path.Combine(directory, Path.GetFileName(deps)), overwrite: true);

        var manifest = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["contractVersion"] = 1,
            ["entryAssembly"] = assemblyName,
            ["entryType"] = moduleType.FullName,
        };
        File.WriteAllText(Path.Combine(directory, ModuleManifest.FileName), manifest.ToJsonString());
        Register(id, "1.0.0", enabled: true, previousVersion: null);
    }

    public void Register(string id, string version, bool enabled, string? previousVersion = null)
    {
        Store.State.Modules[id] = new InstalledModuleState { Version = version, Enabled = enabled, PreviousVersion = previousVersion };
        Store.Save();
    }

    public ModuleInfo Info(string id) => Manager.Modules.Single(m => m.Id == id);

    public void Dispose() => Temp.Dispose();
}
