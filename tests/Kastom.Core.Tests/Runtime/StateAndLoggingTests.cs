using System.IO;
using System.Text.RegularExpressions;
using Kastom.Core.Logging;
using Kastom.Core.Models;
using Kastom.Core.Runtime;

namespace Kastom.Core.Tests.Runtime;

public sealed class JsonLauncherStateStoreTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var store = new JsonLauncherStateStore(_temp.Paths);

        Assert.Empty(store.State.Modules);
        Assert.True(store.State.HotkeysEnabled);
        Assert.Equal(UpdateMode.Notify, store.State.UpdateMode);
    }

    [Fact]
    public void Save_RoundTrips_AndRaisesSaved()
    {
        var paths = _temp.Paths;
        var store = new JsonLauncherStateStore(paths);
        var saved = 0;
        store.Saved += (_, _) => saved++;

        store.State.UpdateMode = UpdateMode.Automatic;
        store.State.PendingRemovals.Add("old");
        store.State.Modules["demo"] = new InstalledModuleState { Version = "1.2.3", PreviousVersion = "1.0.0", Enabled = false };
        store.Save();

        var reloaded = new JsonLauncherStateStore(paths).State;
        Assert.Equal(1, saved);
        Assert.Equal(UpdateMode.Automatic, reloaded.UpdateMode);
        Assert.Equal(["old"], reloaded.PendingRemovals);
        Assert.Equal("1.2.3", reloaded.Modules["demo"].Version);
        Assert.Equal("1.0.0", reloaded.Modules["demo"].PreviousVersion);
        Assert.False(reloaded.Modules["demo"].Enabled);
        Assert.False(File.Exists(paths.LauncherStateFile + ".tmp"));
    }

    [Fact]
    public void CorruptFile_IsRenamed_AndDefaultsUsed()
    {
        var paths = _temp.Paths;
        Directory.CreateDirectory(paths.SettingsRoot);
        File.WriteAllText(paths.LauncherStateFile, "{ це не json");

        var store = new JsonLauncherStateStore(paths);

        Assert.Empty(store.State.Modules);
        Assert.False(File.Exists(paths.LauncherStateFile));
        var backups = Directory.GetFiles(paths.SettingsRoot, "launcher.json.bad-*");
        var backup = Assert.Single(backups);
        Assert.Matches(@"launcher\.json\.bad-\d{14}$", backup);

        store.Save();
        Assert.True(File.Exists(paths.LauncherStateFile));
    }
}

public sealed class FileLoggerTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Writes_FormattedLines_WithException()
    {
        var paths = _temp.Paths;
        var logger = new FileLogger(paths, "demo");

        logger.Info("привіт");
        logger.Warn("увага");
        logger.Error("біда", new InvalidOperationException("boom"));

        var file = Assert.Single(Directory.GetFiles(paths.LogsRoot));
        Assert.Matches(@"kastom-\d{8}\.log$", file);
        var text = File.ReadAllText(file);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] \[demo\] привіт", RegexOptions.Multiline), text);
        Assert.Contains("[WARN] [demo] увага", text);
        Assert.Contains("[ERROR] [demo] біда", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void DeletesLogsOlderThanSevenDays()
    {
        var paths = _temp.Paths;
        Directory.CreateDirectory(paths.LogsRoot);
        var old = Path.Combine(paths.LogsRoot, "kastom-20200101.log");
        var recent = Path.Combine(paths.LogsRoot, "kastom-20200102.log");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-2));

        new FileLogger(paths, "launcher").Info("x");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void NeverThrows_WhenFolderIsUnavailable()
    {
        // Замість папки логів лежить файл: створити папку неможливо.
        var paths = _temp.Paths;
        Directory.CreateDirectory(paths.LocalRoot);
        File.WriteAllText(paths.LogsRoot, "не папка");

        var logger = new FileLogger(paths, "launcher");

        logger.Info("x");
        logger.Error("y", new Exception("z"));
    }
}
