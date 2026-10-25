using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Kastom.Launcher.Services;

/// <summary>
/// Один лаунчер на користувача: mutex тримає перший екземпляр, а наступні шлють йому команду
/// ("show" або "show:&lt;аргумент&gt;") іменованим каналом і виходять. Канал слухається асинхронно, без опитування.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = "Local\\Kastom.Launcher";

    /// <summary>Канал прив'язано до сеанса Windows і до користувача: чужий процес не підставить свій.</summary>
    public static readonly string PipeName = BuildPipeName();

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private static string BuildPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        return $"Kastom.Launcher.{Process.GetCurrentProcess().SessionId}.{sid}";
    }

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Null — лаунчер уже запущено.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static bool SignalExisting(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
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

    private static async Task ListenAsync(Action<string> onMessage, CancellationToken token)
    {
        var maxBackoff = TimeSpan.FromSeconds(30);
        var backoff = TimeSpan.FromSeconds(1);
        var exhausted = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                backoff = TimeSpan.FromSeconds(1);
                exhausted = false;

                // Клієнт, що підключився й мовчить, не має заблокувати канал надовго.
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                readCts.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(readCts.Token);
                if (line is not null && line.StartsWith("show", StringComparison.Ordinal)) onMessage(line);
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
                // Збій каналу (зокрема відмова в доступі): пауза зростає вдвічі до 30 с; якщо й після неї збій — зупиняємось.
                if (exhausted) break;
                try { await Task.Delay(backoff, token); }
                catch (OperationCanceledException) { break; }
                if (backoff >= maxBackoff) exhausted = true;
                else backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, maxBackoff.TotalSeconds));
            }
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
