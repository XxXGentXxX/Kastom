namespace Kastom.Sdk;

/// <summary>Версія контракту між лаунчером і модулями.</summary>
public static class KastomSdk
{
    /// <summary>
    /// Збільшується, коли контракт змінюється несумісно. Модуль, зібраний під іншу версію,
    /// лаунчер не завантажує й пропонує оновити.
    /// </summary>
    public const int ContractVersion = 1;

    /// <summary>Ідентифікатор, під яким свої дії й налаштування реєструє сам лаунчер.</summary>
    public const string LauncherModuleId = "kastom";
}
