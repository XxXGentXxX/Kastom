using System.IO;
using Kastom.Core.Runtime;
using Kastom.Sdk.Settings;

namespace Kastom.Core.Tests.Runtime;

public sealed class ModuleSettingsRegistryTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private ModuleSettingsRegistry Create(KastomPaths? paths = null)
    {
        var registry = new ModuleSettingsRegistry(paths ?? _temp.Paths, "demo");
        registry.Define(new ToggleSetting { Key = "show-greeting", Title = "t", Default = true });
        registry.Define(new RangeSetting { Key = "intensity", Title = "t", Min = 10, Max = 100, Step = 5, Default = 50 });
        registry.Define(new ChoiceSetting
        {
            Key = "accent",
            Title = "t",
            Options = [new ChoiceOption("blue", "Синій"), new ChoiceOption("green", "Зелений")],
            Default = "blue",
        });
        registry.Define(new TextSetting { Key = "nick", Title = "t", Default = "анонім" });
        registry.Define(new ButtonSetting { Key = "say-hello", Title = "t", ActionId = "hello" });
        return registry;
    }

    [Fact]
    public void Get_ReturnsDefaults_WhenNothingStored()
    {
        var registry = Create();

        Assert.True(registry.GetBool("show-greeting"));
        Assert.Equal(50, registry.GetDouble("intensity"));
        Assert.Equal("blue", registry.GetString("accent"));
        Assert.Equal("анонім", registry.GetString("nick"));
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        var registry = Create();

        Assert.Throws<KeyNotFoundException>(() => registry.GetBool("nope"));
        Assert.Throws<KeyNotFoundException>(() => registry.Set("nope", true));
    }

    [Theory]
    [InlineData("Bad")]
    [InlineData("under_score")]
    [InlineData("-a")]
    [InlineData("a--b")]
    [InlineData("")]
    public void Define_RejectsBadKeys(string key)
    {
        var registry = Create();

        Assert.Throws<ArgumentException>(() => registry.Define(new ToggleSetting { Key = key, Title = "t" }));
    }

    [Fact]
    public void Define_RejectsDuplicateKey()
    {
        var registry = Create();

        Assert.Throws<ArgumentException>(() => registry.Define(new TextSetting { Key = "nick", Title = "t" }));
        Assert.Equal(5, registry.Definitions.Count);
    }

    [Theory]
    [InlineData(-5, 10)]
    [InlineData(500, 100)]
    [InlineData(12, 10)]
    [InlineData(13, 15)]
    [InlineData(47.4, 45)]
    [InlineData(47.6, 50)]
    public void Set_Range_ClampsAndRoundsToStepFromMin(double value, double expected)
    {
        var registry = Create();

        registry.Set("intensity", value);

        Assert.Equal(expected, registry.GetDouble("intensity"));
    }

    [Fact]
    public void Set_Choice_ValidatesOptions()
    {
        var registry = Create();

        registry.Set("accent", "green");
        Assert.Equal("green", registry.GetString("accent"));
        Assert.Throws<ArgumentException>(() => registry.Set("accent", "purple"));
        Assert.Equal("green", registry.GetString("accent"));
    }

    [Fact]
    public void Set_WrongKind_Throws()
    {
        var registry = Create();

        Assert.Throws<ArgumentException>(() => registry.Set("show-greeting", 1.0));
        Assert.Throws<ArgumentException>(() => registry.Set("intensity", "50"));
        Assert.Throws<ArgumentException>(() => registry.Set("nick", true));
        Assert.Throws<ArgumentException>(() => registry.Set("say-hello", "x"));
        Assert.Throws<ArgumentException>(() => registry.GetBool("intensity"));
    }

    [Fact]
    public void Values_PersistAcrossInstances()
    {
        var paths = _temp.Paths;
        var first = Create(paths);
        first.Set("show-greeting", false);
        first.Set("intensity", 75);
        first.Set("accent", "green");
        first.Set("nick", "Іван");

        var second = Create(paths);

        Assert.False(second.GetBool("show-greeting"));
        Assert.Equal(75, second.GetDouble("intensity"));
        Assert.Equal("green", second.GetString("accent"));
        Assert.Equal("Іван", second.GetString("nick"));
        Assert.False(File.Exists(paths.ModuleSettingsFile("demo") + ".tmp"));
    }

    [Fact]
    public void StoredValueOfWrongKindOrUnknownOption_FallsBackToDefault()
    {
        var paths = _temp.Paths;
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ModuleSettingsFile("demo"))!);
        File.WriteAllText(paths.ModuleSettingsFile("demo"), """{"show-greeting": "так", "accent": "purple", "intensity": "много"}""");

        var registry = Create(paths);

        Assert.True(registry.GetBool("show-greeting"));
        Assert.Equal("blue", registry.GetString("accent"));
        Assert.Equal(50, registry.GetDouble("intensity"));
    }

    [Fact]
    public void CorruptFile_GivesDefaults()
    {
        var paths = _temp.Paths;
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ModuleSettingsFile("demo"))!);
        File.WriteAllText(paths.ModuleSettingsFile("demo"), "{{{");

        var registry = Create(paths);

        Assert.Equal(50, registry.GetDouble("intensity"));
    }

    [Fact]
    public void Set_RaisesChanged_AfterPersisting()
    {
        var paths = _temp.Paths;
        var registry = Create(paths);
        var events = new List<(string Key, object Value, bool Persisted)>();
        registry.Changed += (_, e) =>
            events.Add((e.Key, e.Value, File.ReadAllText(paths.ModuleSettingsFile("demo")).Contains($"\"{e.Key}\"")));

        registry.Set("show-greeting", false);
        registry.Set("intensity", 47.6);
        registry.Set("accent", "green");

        Assert.Equal(3, events.Count);
        Assert.Equal(("show-greeting", (object)false), (events[0].Key, events[0].Value));
        Assert.Equal(50.0, events[1].Value);
        Assert.Equal("green", events[2].Value);
        Assert.All(events, e => Assert.True(e.Persisted));
    }

    [Fact]
    public void SetCustomPage_StoresFactory()
    {
        var registry = Create();
        Func<System.Windows.FrameworkElement> factory = () => null!;

        registry.SetCustomPage(factory);

        Assert.Same(factory, registry.CustomPageFactory);
    }
}
