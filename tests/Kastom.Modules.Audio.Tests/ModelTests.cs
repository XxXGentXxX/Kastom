using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kastom.Modules.Audio.Model;
using Kastom.Modules.Audio.Resources;
using Kastom.Sdk;
using Kastom.Sdk.Settings;

namespace Kastom.Modules.Audio.Tests;

public sealed class VolumeMathTests
{
    [Theory]
    [InlineData(0.43f, 2, true, 44)]
    [InlineData(0.43f, 2, false, 42)]
    [InlineData(0.44f, 2, true, 46)]
    [InlineData(0.44f, 2, false, 42)]
    [InlineData(0.99f, 5, true, 100)]
    [InlineData(1f, 5, true, 100)]
    [InlineData(0.01f, 5, false, 0)]
    [InlineData(0f, 5, false, 0)]
    [InlineData(0.5f, 0, true, 51)]
    public void StepMovesToNextMultiple(float volume, int step, bool up, int expected) =>
        Assert.Equal(expected, VolumeMath.ToPercent(VolumeMath.Step(volume, step, up)));

    [Theory]
    [InlineData(0.455f, 46)]
    [InlineData(-1f, 0)]
    [InlineData(2f, 100)]
    public void ToPercentRoundsAndClamps(float volume, int expected) => Assert.Equal(expected, VolumeMath.ToPercent(volume));
}

public sealed class AudioOptionsTests
{
    [Fact]
    public void DefaultsMatchIvansAnswers()
    {
        var (_, options) = TestOptions.Create();
        Assert.False(options.SeparateCommunications);
        Assert.True(options.ShowMeters);
        Assert.True(options.TrayIcon);
        Assert.True(options.MicTrayIcon);
        Assert.True(options.Osd);
        Assert.True(options.PerAppOutput);
        Assert.Equal(2, options.VolumeStepPercent);
    }

    [Fact]
    public void RolesTogetherUnlessSeparate()
    {
        var (settings, options) = TestOptions.Create();
        Assert.Equal(DefaultRoles.All, options.RolesFor(communicationsButton: false));

        settings.Set(SettingKeys.SeparateCommunications, true);
        Assert.Equal(DefaultRoles.Normal, options.RolesFor(communicationsButton: false));
        Assert.Equal(DefaultRoles.Communications, options.RolesFor(communicationsButton: true));
    }

    [Fact]
    public void SlotsAreTrimmedAndLoseExe()
    {
        var (_, options) = TestOptions.Create();
        options.SetAppSlot(1, "  Discord.EXE ");
        options.SetMicSlot(2, " Yeti ");
        Assert.Equal("Discord", options.AppSlot(1));
        Assert.Equal("Yeti", options.MicSlot(2));
        Assert.Equal("", options.AppSlot(2));
    }

    [Fact]
    public void ChangedReportsKey()
    {
        var (settings, options) = TestOptions.Create();
        var keys = new List<string>();
        options.Changed += (_, key) => keys.Add(key);
        settings.Set(SettingKeys.MicTrayIcon, false);
        Assert.Equal([SettingKeys.MicTrayIcon], keys);
    }
}

public sealed partial class ModuleDefinitionTests
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [Fact]
    public void SettingKeysAreValidAndButtonsPointToActions()
    {
        var settings = new TestSettingsRegistry();
        AudioSettings.Define(settings);

        Assert.All(settings.Definitions, d => Assert.Matches(IdPattern(), d.Key));
        Assert.All(settings.Definitions, d => Assert.False(string.IsNullOrWhiteSpace(d.Title)));
        var actionIds = typeof(ActionIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();
        Assert.All(settings.Definitions.OfType<ButtonSetting>(), b => Assert.Contains(b.ActionId, actionIds));
    }

    [Fact]
    public void ActionIdsAreValid()
    {
        var ids = typeof(ActionIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        for (var slot = 1; slot <= ActionIds.AppSlotCount; slot++)
            ids.AddRange([ActionIds.AppSlotToggle(slot), ActionIds.AppSlotUp(slot), ActionIds.AppSlotDown(slot)]);
        for (var slot = 1; slot <= ActionIds.MicSlotCount; slot++) ids.Add(ActionIds.MicSlot(slot));

        Assert.All(ids, id => Assert.Matches(IdPattern(), id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ManifestMatchesContract()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModuleFiles.Directory, "module.json")));
        var root = json.RootElement;
        Assert.Equal("audio", root.GetProperty("id").GetString());
        Assert.Equal(KastomSdk.ContractVersion, root.GetProperty("contractVersion").GetInt32());
        Assert.Equal("Kastom.Modules.Audio.dll", root.GetProperty("entryAssembly").GetString());

        var entryType = typeof(Model.AudioSnapshot).Assembly.GetType(root.GetProperty("entryType").GetString()!);
        Assert.NotNull(entryType);
        Assert.True(typeof(IKastomModule).IsAssignableFrom(entryType));
        Assert.True(entryType!.IsPublic);
    }

    [Fact]
    public void EveryStringPropertyHasResxEntry()
    {
        AssertResxMatches(typeof(Strings), "Strings.resx");
    }

    /// <summary>Кожна властивість класу рядків має запис у resx, і навпаки; текст не порожній.</summary>
    internal static void AssertResxMatches(Type accessor, string resxFile)
    {
        var resx = XDocument.Load(Path.Combine(ModuleFiles.Directory, "Resources", resxFile));
        var keys = resx.Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet();
        var properties = accessor.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        Assert.Equal(keys.OrderBy(k => k), properties.Select(p => p.Name).OrderBy(n => n));
        foreach (var property in properties)
        {
            var value = (string)property.GetValue(null)!;
            Assert.NotEqual(property.Name, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }
}
