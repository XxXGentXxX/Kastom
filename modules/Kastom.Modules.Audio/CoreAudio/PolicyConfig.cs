using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>
/// IPolicyConfig — недокументований інтерфейс, через який сама Windows (панель «Звук», Параметри) ставить
/// пристрій за замовчуванням і пише властивості пристрою, яких звичайній програмі не дозволено писати
/// (наприклад, «Прослуховувати цей пристрій»). 🟡: CLSID і IID стабільні з Windows 7, ними користуються
/// SoundSwitch, EarTrumpet, AudioDeviceCmdlets.
/// <para>
/// Виклики йдуть через таблицю методів напряму (вказівники на функції), а не через [ComImport]: так видно,
/// який саме слот викликається, і немає маршалінгу, що міг би приховати невідповідність параметрів.
/// </para>
/// </summary>
internal sealed unsafe class PolicyConfig : IDisposable
{
    private static readonly Guid ClsidPolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid IidPolicyConfig = new("f8679f50-850a-41cf-9c72-430f290290c8");

    // IUnknown (0–2), GetMixFormat 3, GetDeviceFormat 4, ResetDeviceFormat 5, SetDeviceFormat 6,
    // GetProcessingPeriod 7, SetProcessingPeriod 8, GetShareMode 9, SetShareMode 10,
    // GetPropertyValue 11, SetPropertyValue 12, SetDefaultEndpoint 13, SetEndpointVisibility 14.
    private const int SlotGetPropertyValue = 11;
    private const int SlotSetPropertyValue = 12;
    private const int SlotSetDefaultEndpoint = 13;

    private IntPtr _instance;

    private PolicyConfig(IntPtr instance)
    {
        _instance = instance;
    }

    public static PolicyConfig? TryCreate(IModuleLogger log)
    {
        var hr = NativeMethods.CoCreateInstance(
            ClsidPolicyConfigClient, IntPtr.Zero, ComConstants.ClsctxInprocServer, IidPolicyConfig, out var instance);
        if (hr >= 0 && instance != IntPtr.Zero) return new PolicyConfig(instance);

        log.Warn($"IPolicyConfig недоступний (0x{hr:X8}): пристрій за замовчуванням перемикати не вийде.");
        return null;
    }

    private IntPtr* Table => *(IntPtr**)_instance;

    public int SetDefaultEndpoint(string deviceId, ERole role)
    {
        ObjectDisposedException.ThrowIf(_instance == IntPtr.Zero, this);
        fixed (char* id = deviceId)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)Table[SlotSetDefaultEndpoint];
            return method(_instance, id, (int)role);
        }
    }

    /// <summary>
    /// Записати властивість пристрою. Форма виклику (з BOOL bFxStore чи без нього) залежить від
    /// <see cref="PolicyAbi.PropertyArgs"/>, яку перевірено пробою на справжній Windows.
    /// </summary>
    public int SetPropertyValue(string deviceId, PropertyKey key, ref PropVariant value)
    {
        ObjectDisposedException.ThrowIf(_instance == IntPtr.Zero, this);
        return CallProperty(SlotSetPropertyValue, PolicyAbi.PropertyArgs, deviceId, key, ref value);
    }

    /// <summary>Лише для проби: прочитати властивість заданою формою виклику.</summary>
    internal int GetPropertyValueForProbe(PropertyArgs form, string deviceId, PropertyKey key, ref PropVariant value) =>
        CallProperty(SlotGetPropertyValue, form, deviceId, key, ref value);

    private int CallProperty(int slot, PropertyArgs form, string deviceId, PropertyKey key, ref PropVariant value)
    {
        fixed (char* id = deviceId)
        fixed (PropVariant* variant = &value)
        {
            var keyCopy = key;
            switch (form)
            {
                case PropertyArgs.WithFxStore:
                {
                    var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, PropertyKey*, PropVariant*, int>)Table[slot];
                    return method(_instance, id, 0, &keyCopy, variant);
                }

                case PropertyArgs.WithoutFxStore:
                {
                    var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, PropertyKey*, PropVariant*, int>)Table[slot];
                    return method(_instance, id, &keyCopy, variant);
                }

                default:
                    return unchecked((int)0x80004001); // E_NOTIMPL: форма не перевірена — не викликаємо наосліп.
            }
        }
    }

    public void Dispose()
    {
        var instance = Interlocked.Exchange(ref _instance, IntPtr.Zero);
        if (instance != IntPtr.Zero) Marshal.Release(instance);
    }
}

/// <summary>Форма методів Get/SetPropertyValue в IPolicyConfig: публічні описи інтерфейсу розходяться.</summary>
internal enum PropertyArgs
{
    Unknown,

    /// <summary>(PCWSTR deviceId, BOOL bFxStore, const PROPERTYKEY*, PROPVARIANT*).</summary>
    WithFxStore,

    /// <summary>(PCWSTR deviceId, const PROPERTYKEY*, PROPVARIANT*).</summary>
    WithoutFxStore,
}

/// <summary>
/// Що з недокументованих викликів перевірено на справжній Windows окремою пробою (поза процесом лаунчера).
/// Неперевірене модуль не викликає: помилка в таблиці методів валила б увесь лаунчер.
/// <para>
/// Проба 2026-10-25, Windows 11 26H2 (збірка 26300): GetPropertyValue з bFxStore для мікрофона з увімкненим
/// прослуховуванням повернула VT_BOOL true і id пристрою виводу — те саме, що документоване сховище властивостей;
/// без bFxStore — порожнє значення. GetPersistedDefaultAudioEndpoint повернула для Chrome справжній id пристрою.
/// </para>
/// </summary>
internal static class PolicyAbi
{
    /// <summary>Форма Get/SetPropertyValue для «Прослуховувати цей пристрій». Unknown — функцію сховано.</summary>
    public static PropertyArgs PropertyArgs { get; set; } = PropertyArgs.WithFxStore;

    /// <summary>Вивід окремої програми (IAudioPolicyConfigFactory) перевірено. false — функцію сховано.</summary>
    public static bool AppRoutingVerified { get; set; } = true;
}
