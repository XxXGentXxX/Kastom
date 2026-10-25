using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Kastom.Core.Runtime;

/// <summary>
/// Один лаунчер на користувача: mutex тримає перший екземпляр, а наступні шлють йому команду
/// ("show" або "show:&lt;аргумент&gt;") іменованим каналом і виходять. Канал слухається асинхронно, без опитування.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        PipeName = pipeName;
    }

    /// <summary>Канал прив'язано до сеанса Windows і до користувача: чужий процес не підставить свій.</summary>
    public string PipeName { get; }

    /// <summary>Перша пауза після збою каналу; далі вдвічі довша до <see cref="MaxBackoff"/>. Змінюється лише в тестах.</summary>
    internal TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    internal TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Імена mutex і каналу. <paramref name="scope"/> — тека даних пісочниці (--data-dir): її копія лаунчера
    /// живе окремо й не шле команд справжньому лаунчерові. Null — справжній лаунчер.
    /// </summary>
    public static (string Mutex, string Pipe) Names(string? scope)
    {
        var suffix = "";
        if (scope is not null)
        {
            var key = Path.GetFullPath(scope).TrimEnd('\\', '/').ToUpperInvariant();
            suffix = "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        }

        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        return ($"Local\\Kastom.Launcher{suffix}", $"Kastom.Launcher.{Process.GetCurrentProcess().SessionId}.{sid}{suffix}");
    }

    /// <summary>Null — лаунчер уже запущено.</summary>
    public static SingleInstance? TryAcquire(string? scope = null)
    {
        var (mutexName, pipeName) = Names(scope);
        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex, pipeName);
        mutex.Dispose();
        return null;
    }

    public static bool SignalExisting(string message, string? scope = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Names(scope).Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(message);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Повідомлення приходять у довільному потоці.</summary>
    public void StartListening(Action<string> onMessage) => _ = ListenAsync(onMessage, _cts.Token);

    private async Task ListenAsync(Action<string> onMessage, CancellationToken token)
    {
        var backoff = InitialBackoff;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                backoff = InitialBackoff;

                // Клієнт, що підключився й мовчить, не має заблокувати канал надовго.
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                readCts.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(readCts.Token);
                if (line is not null && line.StartsWith("show", StringComparison.Ordinal)) Deliver(onMessage, line);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Таймаут читання: слухаємо далі.
            }
            catch (Exception)
            {
                // Збій каналу (відмова в доступі, ім'я зайняв інший процес): пауза зростає вдвічі до MaxBackoff
                // і далі лишається такою. Канал оживає, щойно збій мине; поки він працює, пауз немає зовсім.
                try { await Task.Delay(backoff, token); }
                catch (OperationCanceledException) { break; }
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    /// <summary>Помилка обробника — не збій каналу: її не можна сплутати з відмовою в доступі й чекати паузу.</summary>
    private static void Deliver(Action<string> onMessage, string line)
    {
        try
        {
            onMessage(line);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Kastom: обробник команди каналу впав: {exception}");
        }
    }

    /// <summary>Звільнити mutex і канал (потрібно перед перезапуском). Викликати в потоці, що створив mutex.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
