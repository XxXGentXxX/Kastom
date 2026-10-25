using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Modules.Audio.Ui;

namespace Kastom.Modules.Audio.Tests.Ui;

/// <summary>Зіставлення пристрою з програмою за назвою в дужках (прохання власника 2026-10-25: NVIDIA Broadcast).</summary>
public sealed class DeviceAppMatchTests
{
    private const string BroadcastPath = @"c:\program files\nvidia corporation\nvidia broadcast\nvidia broadcast.exe";

    private static AudioApp App(string name, string? process, string key = BroadcastPath, bool system = false) => new()
    {
        Key = key,
        DeviceId = "out",
        Name = name,
        Volume = 1,
        IsMuted = false,
        ProcessName = process,
        IsSystemSounds = system,
    };

    private static AudioDevice Device(string name, AudioFlow flow = AudioFlow.Render, params AudioApp[] apps) => new()
    {
        Id = flow == AudioFlow.Render ? "out" : "in",
        Flow = flow,
        Name = name,
        Volume = 0.5f,
        IsMuted = false,
        Apps = apps,
    };

    private static AudioSnapshot Snapshot(params AudioApp[] apps) =>
        new([Device("Навушники", AudioFlow.Render, apps), Device("Speakers (NVIDIA Broadcast)"), Device("Microphone (NVIDIA Broadcast)", AudioFlow.Capture)]);

    [Theory]
    [InlineData("Speakers (NVIDIA Broadcast)", "NVIDIA Broadcast")]
    [InlineData("Динаміки (Realtek(R) Audio)", "Realtek(R) Audio")]
    [InlineData("Microphone (  Yeti  )", "Yeti")]
    public void BracketedNameIsTheTextInsideTheBrackets(string device, string expected) =>
        Assert.Equal(expected, AudioSnapshot.BracketedName(device));

    [Theory]
    [InlineData("Навушники")]
    [InlineData("Навушники ()")]
    [InlineData("Навушники (   )")]
    [InlineData("Навушники )(")]
    [InlineData("")]
    public void NoBracketsOrEmptyBracketsGiveNothing(string device) => Assert.Null(AudioSnapshot.BracketedName(device));

    [Fact]
    public void MatchesByAppNameIgnoringCase()
    {
        var snapshot = Snapshot(App("nvidia BROADCAST", "other"));

        var found = snapshot.FindAppOfDevice(Device("Speakers (NVIDIA Broadcast)"));

        Assert.Equal(BroadcastPath, found!.Key);
    }

    [Fact]
    public void MatchesByProcessNameIgnoringCase()
    {
        var snapshot = Snapshot(App("Щось інше", "NVIDIA Broadcast"));

        var found = snapshot.FindAppOfDevice(Device("Speakers (nvidia broadcast)"));

        Assert.NotNull(found);
    }

    [Fact]
    public void WorksForMicrophonesToo()
    {
        var snapshot = Snapshot(App("NVIDIA Broadcast", "nvidia broadcast"));

        Assert.NotNull(snapshot.FindAppOfDevice(Device("Microphone (NVIDIA Broadcast)", AudioFlow.Capture)));
    }

    [Fact]
    public void NoMatchWhenNameDiffersOrIsOnlyPartOfTheBrackets()
    {
        var snapshot = Snapshot(App("NVIDIA Broadcast", "nvidia broadcast"));

        Assert.Null(snapshot.FindAppOfDevice(Device("Speakers (Realtek)")));
        Assert.Null(snapshot.FindAppOfDevice(Device("Speakers (NVIDIA Broadcast Pro)")));
        Assert.Null(snapshot.FindAppOfDevice(Device("NVIDIA Broadcast")));
    }

    [Fact]
    public void AppWithoutKnownPathIsNotOffered()
    {
        var byPid = Snapshot(App("NVIDIA Broadcast", "nvidia broadcast", key: "pid:4242"));
        var system = Snapshot(App("NVIDIA Broadcast", null, key: "system", system: true));

        Assert.Null(byPid.FindAppOfDevice(Device("Speakers (NVIDIA Broadcast)")));
        Assert.Null(system.FindAppOfDevice(Device("Speakers (NVIDIA Broadcast)")));
    }

    [Fact]
    public void FirstAppWithAPathWinsWhenSeveralMatch()
    {
        var snapshot = Snapshot(App("NVIDIA Broadcast", "nvidia broadcast", key: "pid:1"), App("NVIDIA Broadcast", "nvidia broadcast"));

        Assert.Equal(BroadcastPath, snapshot.FindAppOfDevice(Device("Speakers (NVIDIA Broadcast)"))!.Key);
    }

    [Theory]
    [InlineData(@"c:\program files\app\app.exe", true)]
    [InlineData("pid:1234", false)]
    [InlineData("system", false)]
    [InlineData("", false)]
    public void ExePathIsTheKeyOnlyWhenTheKeyIsAPath(string key, bool hasPath) =>
        Assert.Equal(hasPath ? key : null, App("X", "x", key).ExePath);

    [Fact]
    public void SystemSoundsHaveNoExePath() =>
        Assert.Null(App("Системні звуки", null, key: @"c:\windows\system32\whatever.exe", system: true).ExePath);
}

/// <summary>Пункти меню «⋯» програми й пристрою: відкриття програми, Провідник, параметри звуку.</summary>
public sealed class OpenAppMenuTests
{
    private const string BroadcastPath = @"c:\program files\nvidia corporation\nvidia broadcast\nvidia broadcast.exe";
    private const string BroadcastOutput = "{0.0.0.00000000}.{66666666-6666-6666-6666-666666666666}";
    private const string BroadcastMic = "{0.0.1.00000000}.{77777777-7777-7777-7777-777777777777}";

    /// <summary>Демо-знімок плюс Speakers/Microphone (NVIDIA Broadcast) і програма NVIDIA Broadcast під навушниками.</summary>
    private static MixerFixture WithBroadcast(MixerMode mode = MixerMode.Page, bool appHasPath = true)
    {
        var f = MixerFixture.Create(mode);
        var snapshot = f.Backend.Current;
        var broadcast = new AudioApp
        {
            Key = appHasPath ? BroadcastPath : "pid:4242",
            DeviceId = DemoIds.Headphones,
            Name = "NVIDIA Broadcast",
            Volume = 1,
            IsMuted = false,
            ProcessName = "nvidia broadcast",
            ProcessIds = [4242, 4243],
            IsActive = true,
        };
        f.Backend.Replace(snapshot with
        {
            Devices =
            [
                .. snapshot.Devices.Select(d => d.Id != DemoIds.Headphones ? d : d with { Apps = [.. d.Apps, broadcast] }),
                new AudioDevice { Id = BroadcastOutput, Flow = AudioFlow.Render, Name = "Speakers (NVIDIA Broadcast)", Volume = 1, IsMuted = false },
                new AudioDevice { Id = BroadcastMic, Flow = AudioFlow.Capture, Name = "Microphone (NVIDIA Broadcast)", Volume = 1, IsMuted = false },
            ],
        });
        Sta.DoEvents();
        return f;
    }

    [Fact]
    public void AppMenuHasOpenAndRevealForAppsWithAPath() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        var menu = f.App(DemoIds.Headphones, DemoIds.ChromeKey).BuildMenu();

        Assert.Equal("Відкрити програму", menu.Headers().First());
        Assert.Equal("Показати у Провіднику", menu.Headers().Skip(1).First());
    });

    [Fact]
    public void OpenAndRevealCallTheShellWithThatApp() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        var chrome = f.App(DemoIds.Headphones, DemoIds.ChromeKey);
        var menu = chrome.BuildMenu();

        menu.Item("Відкрити програму").Press();
        menu.Item("Показати у Провіднику").Press();

        Assert.Equal([nameof(IAudioShell.OpenApp), nameof(IAudioShell.RevealApp)], f.Shell.Calls);
        Assert.Equal(DemoIds.ChromeKey, Assert.Single(f.Shell.OpenedApps).Key);
        Assert.Equal([4120, 7712], f.Shell.OpenedApps[0].ProcessIds);
        Assert.Equal(DemoIds.ChromeKey, Assert.Single(f.Shell.RevealedApps).Key);
        Assert.Empty(f.Shell.Errors);
    });

    [Fact]
    public void SystemSoundsGetNeitherItemNorMenu() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        var system = f.Output(DemoIds.Headphones).Apps[^1];

        Assert.Empty(system.BuildMenu().Headers());
        Assert.False(system.IsMoreVisible);
    });

    [Fact]
    public void AppWithoutPathGetsNoOpenItemsButKeepsTheRest() => Sta.Run(() =>
    {
        var f = WithBroadcast(appHasPath: false);
        var row = f.App(DemoIds.Headphones, "pid:4242");

        Assert.Equal(["Вивід (експериментально)", "Призначити на дію"], row.BuildMenu().Headers());
        Assert.True(row.IsMoreVisible);
    });

    [Fact]
    public void AppWithoutPathAndWithoutAnythingElseHasNoMenuButton() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(configure: s => s.Set(SettingKeys.PerAppOutput, false));
        var snapshot = f.Backend.Current;
        var nameless = new AudioApp { Key = "pid:77", DeviceId = DemoIds.Headphones, Name = "Невідома", Volume = 1, IsMuted = false, IsActive = true };
        f.Backend.Replace(snapshot with
        {
            Devices = snapshot.Devices.Select(d => d.Id != DemoIds.Headphones ? d : d with { Apps = [.. d.Apps, nameless] }).ToArray(),
        });
        Sta.DoEvents();

        var row = f.App(DemoIds.Headphones, "pid:77");
        Assert.False(row.IsMoreVisible);
        Assert.Empty(row.BuildMenu().Headers());
    });

    [Fact]
    public void ShellFailureInOpenIsReportedNotThrown() => Sta.Run(() =>
    {
        var f = MixerFixture.Create(MixerMode.Page);
        var host = new MixerHost(f.Backend, f.Options, new ThrowingOpenShell());
        var view = new MixerView(host, MixerMode.Page);
        view.Attach();
        var failing = view.OutputCards.First(c => c.Id == DemoIds.Headphones).Apps.First(a => a.App.Key == DemoIds.ChromeKey);

        failing.BuildMenu().Item("Відкрити програму").Press();
        failing.BuildMenu().Item("Показати у Провіднику").Press();

        var shell = (ThrowingOpenShell)host.Shell;
        Assert.Equal([Strings.OsdCommandFailed, Strings.OsdCommandFailed], shell.Reported);
    });

    [Fact]
    public void OutputCardWithMatchingAppOffersOpenAndSoundSettings() => Sta.Run(() =>
    {
        var f = WithBroadcast();
        var card = f.Output(BroadcastOutput);

        var menu = card.BuildMenu();

        Assert.Equal(["Відкрити NVIDIA Broadcast", "Параметри звуку Windows"], menu.Headers());
        menu.Item("Відкрити NVIDIA Broadcast").Press();
        Assert.Equal(BroadcastPath, Assert.Single(f.Shell.OpenedApps).Key);
        menu.Item("Параметри звуку Windows").Press();
        Assert.Equal([nameof(IAudioShell.OpenApp), nameof(IAudioShell.OpenSoundSettings)], f.Shell.Calls);
    });

    [Fact]
    public void OutputCardWithoutMatchingAppHasOnlySoundSettings() => Sta.Run(() =>
    {
        var f = WithBroadcast();

        Assert.Equal(["Параметри звуку Windows"], f.Output(DemoIds.Headphones).BuildMenu().Headers());
        Assert.Equal(["Параметри звуку Windows"], f.Output(DemoIds.Speakers).BuildMenu().Headers());

        f.Output(DemoIds.Speakers).BuildMenu().Item("Параметри звуку Windows").Press();
        Assert.Equal([nameof(IAudioShell.OpenSoundSettings)], f.Shell.Calls);
    });

    [Fact]
    public void DeviceWhoseAppHasNoPathGetsNoOpenItem() => Sta.Run(() =>
    {
        var f = WithBroadcast(appHasPath: false);

        Assert.Equal(["Параметри звуку Windows"], f.Output(BroadcastOutput).BuildMenu().Headers());
        Assert.Equal(["Призначити на дію", "Параметри звуку Windows"], f.Input(BroadcastMic).BuildMenu().Headers());
    });

    [Fact]
    public void MicCardAddsOpenAppAndSoundSettingsAroundAssign() => Sta.Run(() =>
    {
        var f = WithBroadcast();

        var menu = f.Input(BroadcastMic).BuildMenu();

        Assert.Equal(["Відкрити NVIDIA Broadcast", "Призначити на дію", "Параметри звуку Windows"], menu.Headers());
        menu.Item("Відкрити NVIDIA Broadcast").Press();
        Assert.Equal(BroadcastPath, Assert.Single(f.Shell.OpenedApps).Key);
        Assert.Equal(["Призначити на дію", "Параметри звуку Windows"], f.Input(DemoIds.HeadsetMic).BuildMenu().Headers());
    });

    [Fact]
    public void OutputCardHasTheMoreButtonButCompactHidesIt() => Sta.Run(() =>
    {
        var f = MixerFixture.Create();
        var card = f.Output(DemoIds.Headphones);

        Assert.Equal(System.Windows.Visibility.Visible, card.More.Visibility);
        Assert.Equal("Ще: Навушники (HyperX Cloud II)", System.Windows.Automation.AutomationProperties.GetName(card.More));

        f.Settings.Set(SettingKeys.Compact, true);
        Sta.DoEvents();
        Assert.Equal(System.Windows.Visibility.Collapsed, card.More.Visibility);
        Assert.All(f.View.InputCards, c => Assert.Equal(System.Windows.Visibility.Collapsed, c.More.Visibility));
    });

    private sealed class ThrowingOpenShell : IAudioShell
    {
        public List<string> Reported { get; } = [];

        public void OpenSoundSettings() { }

        public void OpenInputProperties() { }

        public void OpenOutputProperties() { }

        public void OpenAppVolumeSettings() { }

        public void OpenTaskbarSettings() { }

        public void OpenApp(AudioApp app) => throw new InvalidOperationException("не вдалося");

        public void RevealApp(AudioApp app) => throw new InvalidOperationException("не вдалося");

        public void ReportError(string userMessage, Exception? exception) => Reported.Add(userMessage);
    }
}
