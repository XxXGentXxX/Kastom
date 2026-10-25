using System.Reflection;
using System.Runtime.Loader;

namespace Kastom.Core.Runtime;

/// <summary>
/// Окреме середовище завантаження одного модуля: власні залежності з його папки, а Kastom.Sdk
/// і спільні збірки (платформа, WPF) — ті, що вже є в лаунчері.
/// </summary>
internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ModuleLoadContext(string entryAssemblyPath)
        : base($"module:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Kastom.Sdk") return null;
        if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
