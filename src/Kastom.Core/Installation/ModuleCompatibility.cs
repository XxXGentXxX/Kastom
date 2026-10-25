using Kastom.Sdk;

namespace Kastom.Core.Installation;

public static class ModuleCompatibility
{
    /// <summary>Контракт має збігатися з KastomSdk.ContractVersion, а лаунчер — бути не старішим за minLauncherVersion.</summary>
    public static CompatibilityResult Check(int contractVersion, string minLauncherVersion, ModuleVersion launcherVersion)
    {
        if (contractVersion != KastomSdk.ContractVersion)
            return new CompatibilityResult(false, $"Модуль зібрано під контракт v{contractVersion}, а лаунчер підтримує v{KastomSdk.ContractVersion}.");
        if (!ModuleVersion.TryParse(minLauncherVersion, out var minimum))
            return new CompatibilityResult(false, $"Неправильна мінімальна версія лаунчера: '{minLauncherVersion}'.");
        if (launcherVersion < minimum)
            return new CompatibilityResult(false, $"Потрібен лаунчер {minimum} або новіший.");
        return new CompatibilityResult(true);
    }
}
