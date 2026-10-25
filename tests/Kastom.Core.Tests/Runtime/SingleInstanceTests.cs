using System.IO;
using System.IO.Pipes;
using Kastom.Core.Runtime;

namespace Kastom.Core.Tests.Runtime;

public class SingleInstanceTests
{
    // Кожен тест — своя «пісочниця»: імена mutex і каналу не перетинаються ні з лаунчером, ні з іншими тестами.
    private static string NewScope() => Path.Combine(Path.GetTempPath(), "kastom-single-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecondAcquire_InSameScope_ReturnsNull_UntilFirstDisposed()
    {
        var scope = NewScope();
        var first = SingleInstance.TryAcquire(scope);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(scope));

        first.Dispose();
        using var again = SingleInstance.TryAcquire(scope);
        Assert.NotNull(again);
    }

    [Fact]
    public void DifferentScopes_DoNotShareNames()
    {
        var (mutexA, pipeA) = SingleInstance.Names(NewScope());
        var (mutexB, pipeB) = SingleInstance.Names(NewScope());
        var (realMutex, realPipe) = SingleInstance.Names(null);

        Assert.NotEqual(mutexA, mutexB);
        Assert.NotEqual(pipeA, pipeB);
        Assert.NotEqual(realMutex, mutexA);
        Assert.NotEqual(realPipe, pipeA);
        Assert.Equal(@"Local\Kastom.Launcher", realMutex);
    }

    [Fact]
    public async Task Signal_DeliversShowCommand_AndIgnoresOthers()
    {
        var scope = NewScope();
        using var instance = SingleInstance.TryAcquire(scope)!;
        var received = new List<string>();
        var got = new SemaphoreSlim(0);
        instance.StartListening(line =>
        {
            lock (received) received.Add(line);
            got.Release();
        });

        Assert.True(SingleInstance.SignalExisting("hello", scope));
        Assert.True(SingleInstance.SignalExisting("show:demo", scope));
        Assert.True(await got.WaitAsync(TimeSpan.FromSeconds(5)));

        lock (received) Assert.Equal(["show:demo"], received);
    }

    [Fact]
    public async Task HandlerException_DoesNotStopListening()
    {
        var scope = NewScope();
        using var instance = SingleInstance.TryAcquire(scope)!;
        var calls = 0;
        var second = new SemaphoreSlim(0);
        instance.StartListening(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("обробник упав");
            second.Release();
        });

        Assert.True(SingleInstance.SignalExisting("show", scope));
        Assert.True(SingleInstance.SignalExisting("show", scope));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Listener_RecoversAfterLongFailure()
    {
        var scope = NewScope();
        using var instance = SingleInstance.TryAcquire(scope)!;
        instance.InitialBackoff = TimeSpan.FromMilliseconds(20);
        instance.MaxBackoff = TimeSpan.FromMilliseconds(100);

        // Чужий сервер з лімітом в один екземпляр: створити свій канал з цим ім'ям не вийде, доки він живий.
        var squatter = new NamedPipeServerStream(instance.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var got = new SemaphoreSlim(0);
        instance.StartListening(_ => got.Release());

        // Багато разів довше, ніж уся низка пауз (20 + 40 + 80 + 100 мс): стара версія тут здавалася назавжди.
        await Task.Delay(TimeSpan.FromSeconds(1));
        squatter.Dispose();

        var delivered = false;
        for (var attempt = 0; attempt < 20 && !delivered; attempt++)
        {
            delivered = SingleInstance.SignalExisting("show", scope) && await got.WaitAsync(TimeSpan.FromMilliseconds(500));
        }

        Assert.True(delivered);
    }
}
