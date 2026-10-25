using Kastom.Sdk;

namespace Kastom.Core.Runtime;

internal sealed record LoadedModule(IKastomModule Instance, ModuleContext Context, string Name);
