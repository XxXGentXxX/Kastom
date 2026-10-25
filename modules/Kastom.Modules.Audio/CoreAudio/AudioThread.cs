using System.Collections.Concurrent;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.CoreAudio;

/// <summary>
/// Окремий потік MTA, у якому живуть усі об'єкти Core Audio. Виклики COM з потоку інтерфейсу (STA) пішли б
/// через маршалінг між апартаментами, а частина інтерфейсів Core Audio його не підтримує; тож бекенд
/// робить усе тут, а потік інтерфейсу лише ставить роботу в чергу.
/// </summary>
internal sealed class AudioThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly HashSet<Timer> _timers = [];
    private readonly IModuleLogger _log;
    private readonly Thread _thread;

    public AudioThread(string name, IModuleLogger log)
    {
        _log = log;
        _thread = new Thread(Run) { Name = name, IsBackground = true };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool IsCurrent => Thread.CurrentThread == _thread;

    /// <summary>Поставити роботу в чергу. Після Dispose тихо нічого не робить.</summary>
    public bool Post(Action action)
    {
        try
        {
            _queue.Add(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public Task InvokeAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = Post(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        if (!posted) completion.SetException(new ObjectDisposedException(nameof(AudioThread)));
        return completion.Task;
    }

    /// <summary>Виконати один раз через delayMs мілісекунд (у цьому потоці).</summary>
    public void Schedule(Action action, int delayMs)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            lock (_timers) _timers.Remove(timer!);
            timer!.Dispose();
            Post(action);
        });

        // Таймер треба тримати, інакше збирач сміття прибере його раніше, ніж той спрацює.
        lock (_timers) _timers.Add(timer);
        timer.Change(delayMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_timers)
        {
            foreach (var timer in _timers) timer.Dispose();
            _timers.Clear();
        }

        _queue.CompleteAdding();
        if (!IsCurrent) _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        var initialized = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitMultithreaded) == ComConstants.SOk;
        try
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Помилка однієї роботи не має зупиняти потік: інакше звук перестане оновлюватися зовсім.
                    _log.Error("Помилка в потоці звуку.", ex);
                }
            }
        }
        finally
        {
            if (initialized) NativeMethods.CoUninitialize();
        }
    }
}
