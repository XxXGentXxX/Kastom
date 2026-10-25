using Kastom.Modules.Audio.Shell;

namespace Kastom.Modules.Audio.Tests.Module;

public sealed class AudioShellTests
{
    private static (AudioShell Shell, List<string> Launched, FakeLogger Log, FakeNotifications Osd) Create()
    {
        var launched = new List<string>();
        var log = new FakeLogger();
        var osd = new FakeNotifications();
        return (new AudioShell(log, osd, launched.Add), launched, log, osd);
    }

    [Fact]
    public void EachMethodOpensItsOwnAddress()
    {
        var (shell, launched, _, _) = Create();

        shell.OpenSoundSettings();
        shell.OpenInputProperties();
        shell.OpenOutputProperties();
        shell.OpenAppVolumeSettings();
        shell.OpenTaskbarSettings();

        Assert.Equal(
            new[]
            {
                "ms-settings:sound",
                "ms-settings:sound-defaultinputproperties",
                "ms-settings:sound-defaultoutputproperties",
                "ms-settings:apps-volume",
                "ms-settings:taskbar",
            },
            launched);
    }

    [Fact]
    public void LaunchFailureIsLoggedNotThrown()
    {
        var log = new FakeLogger();
        var shell = new AudioShell(log, new FakeNotifications(), _ => throw new InvalidOperationException("немає обробника"));

        shell.OpenSoundSettings();

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("ms-settings:sound", warning);
        Assert.Contains("немає обробника", warning);
    }

    [Fact]
    public void ReportErrorWritesToLogAndShowsHint()
    {
        var (shell, _, log, osd) = Create();
        var failure = new InvalidOperationException("технічна причина");

        shell.ReportError("Не вдалося змінити звук", failure);

        var error = Assert.Single(log.Errors);
        Assert.Equal("Не вдалося змінити звук", error.Message);
        Assert.Same(failure, error.Exception);
        Assert.Equal("Не вдалося змінити звук", Assert.Single(osd.Texts));
    }
}

public sealed class ForegroundAppTests
{
    private const int Own = 100;

    private static WindowInfo Window(int handle, int process, bool visible = true, bool minimized = false, bool tool = false, bool cloaked = false) =>
        new(new IntPtr(handle), process, visible, minimized, tool, cloaked);

    [Fact]
    public void FirstVisibleForeignWindowBehindTheLauncherIsPicked()
    {
        WindowInfo[] below =
        [
            Window(1, Own),
            Window(2, 200, visible: false),
            Window(3, 201, minimized: true),
            Window(4, 202, tool: true),
            Window(5, 203, cloaked: true),
            Window(6, 204),
            Window(7, 205),
        ];

        var picked = ForegroundApp.PickBehindOwn(below, Own);

        Assert.Equal(new IntPtr(6), picked!.Value.Handle);
        Assert.Equal(204, picked.Value.ProcessId);
    }

    [Fact]
    public void NothingSuitableBehindTheLauncherGivesNull()
    {
        WindowInfo[] below = [Window(1, Own), Window(2, 200, visible: false), Window(3, 201, tool: true)];

        Assert.Null(ForegroundApp.PickBehindOwn(below, Own));
        Assert.Null(ForegroundApp.PickBehindOwn([], Own));
    }

    [Fact]
    public void WalkStopsAtTheFirstSuitableWindow()
    {
        var visited = new List<int>();
        IEnumerable<WindowInfo> Below()
        {
            foreach (var handle in new[] { 1, 2, 3 })
            {
                visited.Add(handle);
                yield return Window(handle, handle == 1 ? Own : 300 + handle);
            }
        }

        ForegroundApp.PickBehindOwn(Below(), Own);

        Assert.Equal([1, 2], visited);
    }
}
