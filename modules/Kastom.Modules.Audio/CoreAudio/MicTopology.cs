using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Modules.Audio.Model;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>
/// Підсилення мікрофона (З10) — вузол гучності в топології драйвера, той самий, що на вкладці «Рівні»
/// властивостей мікрофона («Microphone Boost»). Документований DeviceTopology API, але є не в кожного драйвера.
/// </summary>
internal sealed class MicBoostControl(IAudioVolumeLevel level, float minDb, float maxDb, float stepDb) : IDisposable
{
    private IAudioVolumeLevel? _level = level;

    public MicBoost? Read()
    {
        if (_level is null || _level.GetLevel(0, out var current) < 0) return null;
        return new MicBoost(current, minDb, maxDb, stepDb);
    }

    /// <summary>Округлити до кроку драйвера й записати однаково для всіх каналів.</summary>
    public int Set(float levelDb, ref Guid eventContext)
    {
        if (_level is null) return unchecked((int)0x80004005);
        return _level.SetLevelUniform(Quantize(levelDb, minDb, maxDb, stepDb), ref eventContext);
    }

    public static float Quantize(float levelDb, float minDb, float maxDb, float stepDb)
    {
        var clamped = Math.Clamp(levelDb, minDb, maxDb);
        if (stepDb <= 0) return clamped;
        var steps = MathF.Round((clamped - minDb) / stepDb);
        return Math.Clamp(minDb + steps * stepDb, minDb, maxDb);
    }

    public void Dispose()
    {
        Com.Release(_level);
        _level = null;
    }
}

internal static class MicTopology
{
    // Назви вузла підсилення в драйверах (Realtek, USB Audio Class тощо); англійські найпоширеніші.
    private static readonly string[] BoostNames = ["boost", "підсил", "усил", "verstärk"];
    private const int MaxParts = 64;

    /// <summary>
    /// Пройти сигнальний шлях від з'єднувача пристрою до входу (EnumPartsIncoming) і знайти вузол гучності з назвою
    /// «підсилення». Нічого не знайдено або драйвер не дає топології → null.
    /// </summary>
    public static MicBoostControl? FindBoost(IMMDevice device)
    {
        var toRelease = new List<object>();
        try
        {
            var topology = Com.TryActivate<IDeviceTopology>(device, ComConstants.IidDeviceTopology);
            if (topology is null) return null;
            toRelease.Add(topology);

            if (topology.GetConnector(0, out var connector) < 0 || connector is null) return null;
            toRelease.Add(connector);
            if (connector.GetConnectedTo(out var adapterConnector) < 0 || adapterConnector is null) return null;
            toRelease.Add(adapterConnector);
            if (adapterConnector is not IPart start) return null;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<IPart>();
            queue.Enqueue(start);
            while (queue.Count > 0 && visited.Count < MaxParts)
            {
                var part = queue.Dequeue();
                if (part.GetGlobalId(out var globalId) < 0 || !visited.Add(globalId)) continue;

                if (TryAsBoost(part) is { } boost) return boost;

                if (part.EnumPartsIncoming(out var incoming) < 0 || incoming is null) continue;
                toRelease.Add(incoming);
                if (incoming.GetCount(out var count) < 0) continue;
                for (var i = 0; i < count; i++)
                {
                    if (incoming.GetPart(i, out var next) < 0 || next is null) continue;
                    toRelease.Add(next);
                    queue.Enqueue(next);
                }
            }

            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            foreach (var item in toRelease) Com.Release(item);
        }
    }

    private static MicBoostControl? TryAsBoost(IPart part)
    {
        if (part.GetPartType(out var type) < 0 || type != ComConstants.PartTypeSubunit) return null;
        if (part.GetName(out var name) < 0 || !IsBoostName(name)) return null;

        var iid = ComConstants.IidAudioVolumeLevel;
        if (part.Activate(ComConstants.ClsctxAll, ref iid, out var control) < 0) return null;
        if (control is not IAudioVolumeLevel level)
        {
            Com.Release(control);
            return null;
        }

        if (level.GetLevelRange(0, out var min, out var max, out var step) < 0 || max <= min)
        {
            Com.Release(level);
            return null;
        }

        return new MicBoostControl(level, min, max, step);
    }

    /// <summary>
    /// Пристрій під'єднано через Bluetooth: фільтр драйвера, до якого веде з'єднувач пристрою, має шлях
    /// \\?\BTHENUM#…, \\?\BTHHFENUM#… чи \\?\BTHLEDEVICE#…. Не вдалося з'ясувати — вважаємо, що ні.
    /// </summary>
    public static bool IsBluetooth(IMMDevice device)
    {
        var toRelease = new List<object>();
        try
        {
            var topology = Com.TryActivate<IDeviceTopology>(device, ComConstants.IidDeviceTopology);
            if (topology is null) return false;
            toRelease.Add(topology);
            if (topology.GetConnector(0, out var connector) < 0 || connector is null) return false;
            toRelease.Add(connector);
            return connector.GetDeviceIdConnectedTo(out var filter) >= 0 && IsBluetoothPath(filter);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            foreach (var item in toRelease) Com.Release(item);
        }
    }

    internal static bool IsBluetoothPath(string? filterPath) =>
        filterPath is not null && filterPath.StartsWith(@"\\?\BTH", StringComparison.OrdinalIgnoreCase);

    internal static bool IsBoostName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && BoostNames.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase));
}
