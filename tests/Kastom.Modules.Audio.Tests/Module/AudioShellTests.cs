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
