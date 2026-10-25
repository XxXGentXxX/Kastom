using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>
/// Вивід окремої програми на окремий пристрій (З7, 🟡 експериментально) через недокументовану WinRT-фабрику
/// Windows.Media.Internal.AudioPolicyConfig — той самий механізм, яким користується сторінка
/// Параметри → Звук → Мікшер гучності. Інтерфейс уже мінявся (Windows 11 21H2, збірка 21390): тоді Microsoft
/// змінила й IID, тож якщо таблиця методів знову зміниться, QueryInterface за старим IID просто не вдасться
/// і функцію буде сховано, а не викликано наосліп.
/// </summary>
internal sealed unsafe class AppRouting : IDisposable
{
    private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
    private static readonly Guid IidFactory21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid IidFactoryOld = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    // IUnknown (0–2), IInspectable (3–5), далі 19 методів гучності груп і чат-програм (6–24),
    // SetPersistedDefaultAudioEndpoint 25, GetPersistedDefaultAudioEndpoint 26,
    // ClearAllPersistedApplicationDefaultEndpoints 27 — порядок з EarTrumpet (AudioPolicyConfig.cs).
    private const int SlotSetPersisted = 25;
    private const int SlotGetPersisted = 26;
    private const int SlotClearAll = 27;

    private IntPtr _factory;

    private AppRouting(IntPtr factory)
    {
        _factory = factory;
    }

    public static AppRouting? TryCreate(IModuleLogger log)
    {
        var iid = Environment.OSVersion.Version.Build >= 21390 ? IidFactory21H2 : IidFactoryOld;
        var hr = NativeMethods.WindowsCreateString(ClassName, ClassName.Length, out var className);
        if (hr < 0) return null;
        try
        {
            hr = NativeMethods.RoGetActivationFactory(className, iid, out var factory);
            if (hr >= 0 && factory != IntPtr.Zero) return new AppRouting(factory);
            log.Info($"Вивід окремої програми недоступний у цій Windows (0x{hr:X8}).");
            return null;
        }
        finally
        {
            NativeMethods.WindowsDeleteString(className);
        }
    }

    private IntPtr* Table => *(IntPtr**)_factory;

    /// <summary>Направити процес на пристрій (id у форматі <see cref="DeviceIds.Pack"/>); null — скинути на типовий.</summary>
    public int SetPersisted(int processId, EDataFlow flow, ERole role, string? packedDeviceId)
    {
        ObjectDisposedException.ThrowIf(_factory == IntPtr.Zero, this);
        var hstring = IntPtr.Zero;
        if (packedDeviceId is not null)
        {
            var created = NativeMethods.WindowsCreateString(packedDeviceId, packedDeviceId.Length, out hstring);
            if (created < 0) return created;
        }

        try
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)Table[SlotSetPersisted];
            return method(_factory, (uint)processId, (int)flow, (int)role, hstring);
        }
        finally
        {
            if (hstring != IntPtr.Zero) NativeMethods.WindowsDeleteString(hstring);
        }
    }

    /// <summary>Куди процес направлено вручну; null — нікуди (типовий пристрій) або помилка.</summary>
    public string? GetPersisted(int processId, EDataFlow flow, ERole role, out int hresult)
    {
        ObjectDisposedException.ThrowIf(_factory == IntPtr.Zero, this);
        var hstring = IntPtr.Zero;
        var method = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)Table[SlotGetPersisted];
        hresult = method(_factory, (uint)processId, (int)flow, (int)role, &hstring);
        if (hresult < 0 || hstring == IntPtr.Zero) return null;
        try
        {
            var buffer = NativeMethods.WindowsGetStringRawBuffer(hstring, out var length);
            return length == 0 ? null : new string((char*)buffer, 0, length);
        }
        finally
        {
            NativeMethods.WindowsDeleteString(hstring);
        }
    }

    public int ClearAll()
    {
        ObjectDisposedException.ThrowIf(_factory == IntPtr.Zero, this);
        var method = (delegate* unmanaged[Stdcall]<IntPtr, int>)Table[SlotClearAll];
        return method(_factory);
    }

    public void Dispose()
    {
        var factory = Interlocked.Exchange(ref _factory, IntPtr.Zero);
        if (factory != IntPtr.Zero) Marshal.Release(factory);
    }
}
