using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.CoreAudio;

internal static class Com
{
    /// <summary>Звільнити об'єкт COM одразу, не чекаючи збирача сміття (сеанси й пристрої тримають ресурси служби звуку).</summary>
    public static void Release(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance)) return;
        try
        {
            Marshal.ReleaseComObject(instance);
        }
        catch (Exception)
        {
            // Об'єкт уже відключено — звільняти нічого.
        }
    }

    public static T Activate<T>(IMMDevice device, Guid iid) where T : class =>
        (T)device.Activate(ref iid, ComConstants.ClsctxAll, IntPtr.Zero);

    public static T? TryActivate<T>(IMMDevice device, Guid iid) where T : class
    {
        try
        {
            return device.Activate(ref iid, ComConstants.ClsctxAll, IntPtr.Zero) as T;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>HRESULT помилки → виняток команди; код Windows лишається у вкладеному COMException.</summary>
    public static void Check(int hresult, string what)
    {
        if (hresult >= 0) return;
        throw new AudioCommandException($"{what}: 0x{hresult:X8}", new COMException(what, hresult));
    }

    /// <summary>Виняток означає, що зникла звукова служба (а не один пристрій чи неправильна команда).</summary>
    public static bool IsServiceGone(Exception exception) =>
        exception switch
        {
            COMException com => ComConstants.IsServiceGone(com.HResult),
            InvalidComObjectException => true,
            AudioCommandException { InnerException: { } inner } => IsServiceGone(inner),
            _ => false,
        };

    /// <summary>Виняток означає, що зник чи змінився один пристрій.</summary>
    public static bool IsDeviceGone(Exception exception) =>
        exception switch
        {
            COMException com => ComConstants.IsDeviceGone(com.HResult),
            AudioCommandException { InnerException: { } inner } => IsDeviceGone(inner),
            _ => false,
        };

    public static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) < 0) return null;
        try
        {
            return value.AsString();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }

    public static bool? ReadBool(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) < 0) return null;
        try
        {
            return value.AsBool();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }
}

/// <summary>
/// Ідентифікатори пристроїв у вигляді, якого чекає фабрика AudioPolicyConfig (вивід окремої програми):
/// "\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{e6327cad-dcec-4949-ae8a-991e976a79d2}". Так само їх складає EarTrumpet.
/// </summary>
internal static class DeviceIds
{
    private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureInterface = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";

    public static string Pack(string deviceId, EDataFlow flow) =>
        MmDevApiToken + deviceId + (flow == EDataFlow.Capture ? CaptureInterface : RenderInterface);

    /// <summary>Назад до id MMDevice; порожній чи чужий рядок → null.</summary>
    public static string? Unpack(string? packed)
    {
        if (string.IsNullOrEmpty(packed)) return null;
        if (!packed.StartsWith(MmDevApiToken, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = packed[MmDevApiToken.Length..];
        var hash = rest.LastIndexOf('#');
        return hash > 0 ? rest[..hash] : null;
    }
}

/// <summary>Звідки взявся звуковий сеанс: шлях до exe, опис файлу, ім'я процесу.</summary>
internal static unsafe class ProcessInfo
{
    public static string? GetPath(int processId)
    {
        if (processId <= 0) return null;
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            var size = capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>«Google Chrome» з властивостей файлу; null, якщо опису немає.</summary>
    public static string? GetDescription(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? null : description;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? GetProcessName(string? path, int processId)
    {
        if (path is not null) return Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (processId <= 0) return null;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Назва сеансу: "@файл,-id" і "@{пакет?ms-resource://…}" розгортаються в текст; невдача → null.</summary>
    public static string? ResolveDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!name.StartsWith('@')) return name.Trim();
        const int capacity = 512;
        var buffer = stackalloc char[capacity];
        buffer[0] = '\0';
        if (NativeMethods.SHLoadIndirectString(name, buffer, capacity, IntPtr.Zero) < 0) return null;
        var resolved = new string(buffer).Trim();
        return resolved.Length == 0 ? null : resolved;
    }
}
