using System.IO;
using Kastom.Modules.Audio.Model;
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

    private const string AppPath = @"c:\program files
vidia corporation
vidia broadcast
vidia broadcast.exe";

    private static AudioApp App(string key = AppPath, bool system = false, params int[] pids) => new()
    {
        Key = key,
        DeviceId = "out",
        Name = "NVIDIA Broadcast",
        Volume = 1,
        IsMuted = false,
        IsSystemSounds = system,
        ProcessIds = pids,
    };

    [Fact]
    public void OpenAppShowsTheExistingWindowAndStartsNothing()
    {
        var launched = new List<string>();
        var asked = new List<IReadOnlyList<int>>();
        var shell = new AudioShell(new FakeLogger(), new FakeNotifications(), launched.Add, ids =>
        {
            asked.Add(ids);
            return true;
        });

        shell.OpenApp(App(pids: [10, 20]));

        Assert.Equal([10, 20], Assert.Single(asked));
        Assert.Empty(launched);
    }

    [Fact]
    public void OpenAppStartsTheSameExeWhenThereIsNoWindow()
    {
        var launched = new List<string>();
        var shell = new AudioShell(new FakeLogger(), new FakeNotifications(), launched.Add, _ => false);

        shell.OpenApp(App(pids: [10]));

        Assert.Equal([AppPath], launched);
    }

    [Fact]
    public void OpenAppStartsTheExeWhenWindowLookupFailsAndLogsIt()
    {
        var launched = new List<string>();
        var log = new FakeLogger();
        var shell = new AudioShell(log, new FakeNotifications(), launched.Add, _ => throw new InvalidOperationException("нема доступу"));

        shell.OpenApp(App(pids: [10]));

        Assert.Equal([AppPath], launched);
        Assert.Contains("нема доступу", Assert.Single(log.Warnings));
    }

    [Fact]
    public void OpenAppStartFailureIsLoggedNotThrown()
    {
        var log = new FakeLogger();
        var shell = new AudioShell(log, new FakeNotifications(), _ => throw new InvalidOperationException("не запускається"), _ => false);

        shell.OpenApp(App());

        Assert.Contains("не запускається", Assert.Single(log.Warnings));
    }

    [Theory]
    [InlineData("pid:4242", false)]
    [InlineData("system", false)]
    [InlineData(AppPath, true)]
    public void AppsWithoutPathAndSystemSoundsAreNeverOpenedOrRevealed(string key, bool system)
    {
        var launched = new List<string>();
        var started = new List<(string File, string Args)>();
        var windowLookups = 0;
        var shell = new AudioShell(
            new FakeLogger(), new FakeNotifications(), launched.Add, _ => { windowLookups++; return true; }, (file, args) => started.Add((file, args)));

        shell.OpenApp(App(key, system));
        shell.RevealApp(App(key, system));

        Assert.Equal(0, windowLookups);
        Assert.Empty(started);
        Assert.Empty(launched);
    }

    [Fact]
    public void RevealSelectsTheExeInExplorer()
    {
        var started = new List<(string File, string Args)>();
        var shell = new AudioShell(new FakeLogger(), new FakeNotifications(), startProcess: (file, args) => started.Add((file, args)));

        shell.RevealApp(App());

        var call = Assert.Single(started);

        // Повний шлях до Провідника: інакше CreateProcess спершу шукав би explorer.exe у поточній теці.
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), call.File);
        Assert.True(Path.IsPathFullyQualified(call.File));
        Assert.Equal($"/select,\"{AppPath}\"", call.Args);
        Assert.Equal($"/select,\"{AppPath}\"", AudioShell.RevealArguments(AppPath));
    }

    [Fact]
    public void RevealFailureIsLoggedNotThrown()
    {
        var log = new FakeLogger();
        var shell = new AudioShell(log, new FakeNotifications(), startProcess: (_, _) => throw new InvalidOperationException("немає explorer"));

        shell.RevealApp(App());

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("немає explorer", warning);
        Assert.Contains(AppPath, warning);
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
    [Theory]
    [InlineData(@"c:\program files\google\chromepplication\chrome.exe", true)]
    [InlineData("system", false)]
    [InlineData("pid:1234", false)]
    [InlineData(@"relativepp.exe", false)]
    [InlineData(@"c:\docs
otes.txt", false)]
    [InlineData("https://example.com/app.exe", false)]
    public void OnlyFullExePathsCanBeOpened(string key, bool isExe)
    {
        var app = new Model.AudioApp { Key = key, DeviceId = "d", Name = "x", Volume = 1, IsMuted = false };
        Assert.Equal(isExe ? key : null, app.ExePath);
    }

    [Fact]
    public void FailedLaunchIsShownOnScreen()
    {
        var osd = new FakeNotifications();
        var shell = new AudioShell(new FakeLogger(), osd, _ => throw new InvalidOperationException("не запускається"), _ => false);

        shell.OpenApp(App(pids: [10]));

        Assert.Contains(Resources.Strings.OsdOpenAppFailed, osd.Texts);
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
