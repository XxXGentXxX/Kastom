using System.IO;
using System.Text.Json;
using Kastom.Core.Abstractions;
using Kastom.Core.Hotkeys;
using Kastom.Core.Models;
using Kastom.Core.Runtime;
using Kastom.Core.Tests.Runtime;
using Kastom.Sdk;
using Kastom.Sdk.Hotkeys;

namespace Kastom.Core.Tests.Hotkeys;

public sealed class LegacyHotkeyMigrationTests : IDisposable
{
    // Файл лаунчера 0.1.2: повні поєднання, модулі, вимикач.
    private const string OldFile = """
        {
          "modules": { "audio": { "version": "1.1.0", "enabled": true } },
          "pendingRemovals": [],
          "hotkeys": {
            "kastom/open": "Ctrl+Alt+K",
            "kastom/quick-panel": "Ctrl+Alt+Q",
            "audio/mute": "Ctrl+Alt+M",
            "audio/next": "Ctrl+Alt+N",
            "audio/odd": "Ctrl+Shift+O",
            "audio/off": "",
            "video/cam": "Shift+Win+C",
            "video/junk": "Ctrl+Nonsense"
          },
          "hotkeysEnabled": false,
          "theme": "dark"
        }
        """;

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    [Fact]
    public void Run_turns_full_gestures_into_addresses_and_keys()
    {
        var state = new LauncherState();
        foreach (var line in OldHotkeys()) state.Hotkeys[line.Key] = line.Value;
        var log = new FakeLogger();

        var migrated = LegacyHotkeyMigration.Run(state, log);

        Assert.True(migrated);
        Assert.Empty(state.Hotkeys);
        Assert.Equal("Ctrl+Alt", state.HotkeyAddresses["kastom"]);
        Assert.Equal("Ctrl+Alt", state.HotkeyAddresses["audio"]); // два голоси проти одного
        Assert.Equal("Shift+Win", state.HotkeyAddresses["video"]);
        Assert.Equal("K", state.HotkeyKeys["kastom/open"]);
        Assert.Equal("O", state.HotkeyKeys["audio/odd"]); // модифікатори меншості відкинуто, клавішу збережено
        Assert.Equal("", state.HotkeyKeys["audio/off"]);
        Assert.DoesNotContain("video/junk", state.HotkeyKeys.Keys);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void Run_does_nothing_without_old_entries_and_keeps_new_ones()
    {
        var state = new LauncherState();
        state.HotkeyAddresses["audio"] = "Alt+Shift";
        state.HotkeyKeys["audio/mute"] = "X";

        Assert.False(LegacyHotkeyMigration.Run(state, new FakeLogger()));
        Assert.Equal("Alt+Shift", state.HotkeyAddresses["audio"]);
        Assert.Equal("X", state.HotkeyKeys["audio/mute"]);
    }

    [Fact]
    public void Run_does_not_overwrite_new_format_entries()
    {
        var state = new LauncherState();
        state.Hotkeys["audio/mute"] = "Ctrl+Alt+M";
        state.HotkeyAddresses["audio"] = "Alt+Shift";
        state.HotkeyKeys["audio/mute"] = "X";

        LegacyHotkeyMigration.Run(state, new FakeLogger());

        Assert.Equal("Alt+Shift", state.HotkeyAddresses["audio"]);
        Assert.Equal("X", state.HotkeyKeys["audio/mute"]);
    }

    [Fact]
    public void New_launcher_reads_the_old_file_and_keeps_every_working_gesture()
    {
        WriteOldFile();
        var f = StartManagerOn(_temp);

        Assert.False(f.State.State.HotkeysEnabled); // чужі поля старого файлу читаються як і раніше
        Assert.Equal(LauncherTheme.Dark, f.State.State.Theme);
        Assert.Equal("1.1.0", f.State.State.Modules["audio"].Version);

        // Вимкнено загальним вимикачем, але ефективні поєднання ті самі, що були в старому файлі.
        Assert.Equal("Ctrl+Alt+K", f.Gesture("kastom/open"));
        Assert.Equal("Ctrl+Alt+Q", f.Gesture("kastom/quick-panel"));
        Assert.Equal("Ctrl+Alt+M", f.Gesture("audio/mute"));
        Assert.Equal("Ctrl+Alt+O", f.Gesture("audio/odd"));
        Assert.Equal("Shift+Win+C", f.Gesture("video/cam"));
        Assert.Null(f.Manager.Bindings.Single(b => b.Action.GlobalId == "audio/off").Gesture);
        f.Dispose();
    }

    [Fact]
    public void File_after_migration_has_the_new_format_and_a_second_start_gives_the_same_result()
    {
        WriteOldFile();
        var first = StartManagerOn(_temp);
        var expected = first.Manager.Bindings.ToDictionary(b => b.Action.GlobalId, b => b.Gesture?.ToString());
        first.Dispose();

        using var document = JsonDocument.Parse(File.ReadAllText(_temp.Paths.LauncherStateFile));
        var root = document.RootElement;
        Assert.Empty(root.GetProperty("hotkeys").EnumerateObject());
        Assert.Equal("Ctrl+Alt", root.GetProperty("hotkeyAddresses").GetProperty("kastom").GetString());
        Assert.Equal("Shift+Win", root.GetProperty("hotkeyAddresses").GetProperty("video").GetString());
        Assert.Equal("M", root.GetProperty("hotkeyKeys").GetProperty("audio/mute").GetString());
        Assert.Equal("", root.GetProperty("hotkeyKeys").GetProperty("audio/off").GetString());
        Assert.False(root.GetProperty("hotkeysEnabled").GetBoolean());
        Assert.Equal("dark", root.GetProperty("theme").GetString());

        var second = StartManagerOn(_temp);
        Assert.Equal(expected, second.Manager.Bindings.ToDictionary(b => b.Action.GlobalId, b => b.Gesture?.ToString()));
        Assert.Equal(0, second.Saves); // нічого нового переносити не треба
        second.Dispose();
    }

    [Fact]
    public void Migrated_start_registers_gestures_when_hotkeys_are_enabled()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temp.Paths.LauncherStateFile)!);
        File.WriteAllText(_temp.Paths.LauncherStateFile, OldFile.Replace("\"hotkeysEnabled\": false", "\"hotkeysEnabled\": true"));

        var f = StartManagerOn(_temp);

        Assert.Equal(HotkeyBindingStatus.Active, f.Manager.Bindings.Single(b => b.Action.GlobalId == "kastom/open").Status);
        Assert.Contains(new HotkeyGesture(HotkeyModifiers.Shift | HotkeyModifiers.Win, 'C'), f.Backend.Registered.Values);
        f.Dispose();
    }

    private void WriteOldFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temp.Paths.LauncherStateFile)!);
        File.WriteAllText(_temp.Paths.LauncherStateFile, OldFile);
    }

    private static Dictionary<string, string> OldHotkeys()
    {
        using var document = JsonDocument.Parse(OldFile);
        return document.RootElement.GetProperty("hotkeys").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private static Harness StartManagerOn(TempDir temp)
    {
        var store = new CountingStore(new JsonLauncherStateStore(temp.Paths));
        var harness = new Harness(store);
        foreach (var (module, name, action, key) in new[]
        {
            (KastomSdk.LauncherModuleId, "Лаунчер", "open", 'K'),
            (KastomSdk.LauncherModuleId, "Лаунчер", "quick-panel", 'Q'),
            ("audio", "Звук", "mute", 'M'),
            ("audio", "Звук", "next", 'N'),
            ("audio", "Звук", "odd", 'O'),
            ("audio", "Звук", "off", 'F'),
            ("video", "Фільми", "cam", 'C'),
            ("video", "Фільми", "junk", 'J'),
        })
        {
            // Типові клавіші дій є, але старий файл важливіший: що користувач прибрав чи змінив, те й діє.
            harness.Catalog.Add(Action(module, action, new HotkeyGesture(CtrlAlt, key), name));
        }

        harness.Start();
        return harness;
    }

    private sealed class Harness : IDisposable
    {
        public Harness(CountingStore state) => State = state;

        public CountingStore State { get; }
        public FakeCatalog Catalog { get; } = new();
        public FakeBackend Backend { get; } = new();
        public FakeLogger Log { get; } = new();
        public HotkeyManager Manager { get; private set; } = null!;

        public int Saves => State.SaveCount;

        public void Start()
        {
            Manager = new HotkeyManager(Catalog, State, Backend, Log);
        }

        public string? Gesture(string globalId) => Manager.Bindings.Single(b => b.Action.GlobalId == globalId).Gesture?.ToString();

        public void Dispose() => Manager.Dispose();
    }

    private sealed class CountingStore(ILauncherStateStore inner) : ILauncherStateStore
    {
        public int SaveCount { get; private set; }

        public LauncherState State => inner.State;

        public event EventHandler? Saved
        {
            add => inner.Saved += value;
            remove => inner.Saved -= value;
        }

        public void Save()
        {
            SaveCount++;
            inner.Save();
        }
    }

    private static RegisteredAction Action(string moduleId, string actionId, HotkeyGesture defaultHotkey, string moduleName) =>
        new(moduleId, moduleName, new Kastom.Sdk.Actions.ModuleAction
        {
            Id = actionId,
            Title = actionId,
            DefaultHotkey = defaultHotkey,
            Execute = _ => Task.CompletedTask,
        });
}
