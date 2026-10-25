using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Kastom.Core.Abstractions;
using Kastom.Core.Catalog;
using Kastom.Core.Installation;
using Kastom.Core.Models;

namespace Kastom.Core.Tests.Delivery;

/// <summary>Значок модуля: module.json, CatalogTool, перевіряльник каталогу, установник, сумісність зі старою моделлю.</summary>
public class ModuleIconTests
{
    private static readonly ModuleVersion Launcher = ModuleVersion.Parse("1.0.0");

    /// <summary>Файл, що проходить перевірки значка: підпис PNG і трохи байтів (піксельні дані ніхто не розбирає).</summary>
    internal static byte[] Png(int length = 100)
    {
        var data = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        for (var i = 8; i < data.Length; i++) data[i] = (byte)i;
        return data;
    }

    private static string ManifestWithIcon(string id, string version, string? icon) =>
        JsonSerializer.Serialize(
            new { id, name = "Тест " + id, version, contractVersion = 1, entryAssembly = "mod.dll", entryType = "Demo.Module", icon },
            KastomJson.Options);

    private static CatalogEntry EntryWithIcon(string id, string? icon)
    {
        var plain = DeliveryKit.PlainEntry(id);
        return new CatalogEntry
        {
            Id = plain.Id,
            Name = plain.Name,
            Version = plain.Version,
            ContractVersion = plain.ContractVersion,
            Url = plain.Url,
            Sha256 = plain.Sha256,
            Size = plain.Size,
            Icon = icon,
        };
    }

    // ---- 1. Шлях і вміст значка ----

    [Theory]
    [InlineData("icon.png")]
    [InlineData("assets/icon.png")]
    [InlineData("a/b/c.png")]
    public void ValidatePath_SafePaths_AreAccepted(string path) => Assert.Null(ModuleIcons.ValidatePath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../icon.png")]
    [InlineData("a/../../icon.png")]
    [InlineData("..")]
    [InlineData("/icon.png")]
    [InlineData("\\icon.png")]
    [InlineData("assets\\icon.png")]
    [InlineData("C:/icon.png")]
    [InlineData("C:icon.png")]
    [InlineData("assets/")]
    [InlineData("a//b.png")]
    [InlineData("./icon.png")]
    public void ValidatePath_UnsafePaths_AreRejected(string? path) => Assert.NotNull(ModuleIcons.ValidatePath(path));

    [Fact]
    public void ValidateData_ChecksSignatureAndSize()
    {
        Assert.Null(ModuleIcons.ValidateData(Png()));
        Assert.Null(ModuleIcons.ValidateData(Png(ModuleIcons.MaxSize)));
        Assert.NotNull(ModuleIcons.ValidateData(Png(ModuleIcons.MaxSize + 1)));
        Assert.NotNull(ModuleIcons.ValidateData([]));
        Assert.NotNull(ModuleIcons.ValidateData("GIF89a-not-a-png-file"u8));
        Assert.NotNull(ModuleIcons.ValidateData([0x89, 0x50, 0x4E, 0x47]));
    }

    [Fact]
    public void TryDecode_Base64Rules()
    {
        Assert.True(ModuleIcons.TryDecode(null, out var none, out _));
        Assert.Null(none);

        var png = Png();
        Assert.True(ModuleIcons.TryDecode(Convert.ToBase64String(png), out var decoded, out _));
        Assert.Equal(png, decoded);

        Assert.False(ModuleIcons.TryDecode("це не base64!", out var bad, out var problem));
        Assert.Null(bad);
        Assert.NotNull(problem);
        Assert.False(ModuleIcons.TryDecode(Convert.ToBase64String("not a png at all"u8), out _, out _));
        Assert.False(ModuleIcons.TryDecode(Convert.ToBase64String(Png(ModuleIcons.MaxSize + 1)), out _, out _));
    }

    [Fact]
    public void Manifest_ReadsOptionalIconField()
    {
        var withIcon = JsonSerializer.Deserialize<ModuleManifest>(ManifestWithIcon("demo", "1.0.0", "icon.png"), KastomJson.Options)!;
        var without = JsonSerializer.Deserialize<ModuleManifest>(DeliveryKit.ManifestJson("demo", "1.0.0"), KastomJson.Options)!;

        Assert.Equal("icon.png", withIcon.Icon);
        Assert.Null(without.Icon);
    }

    // ---- 2. Перевіряльник каталогу ----

    [Fact]
    public void Verifier_ValidIcon_IsKept()
    {
        using var kit = new DeliveryKit();
        var png = Png();
        var json = DeliveryKit.CatalogBytes([EntryWithIcon("alpha", Convert.ToBase64String(png)), EntryWithIcon("beta", null)]);

        var catalog = kit.Verifier.Verify(json, kit.Sign(json), allowFileUrls: false);

        Assert.Equal(png, Convert.FromBase64String(catalog.Modules.Single(m => m.Id == "alpha").Icon!));
        Assert.Null(catalog.Modules.Single(m => m.Id == "beta").Icon);
        Assert.Empty(catalog.IconProblems);
    }

    public static TheoryData<string> BrokenIcons() => new()
    {
        "це не base64!",
        Convert.ToBase64String("not a png at all"u8),
        Convert.ToBase64String(Png(ModuleIcons.MaxSize + 1)),
        "",
    };

    [Theory]
    [MemberData(nameof(BrokenIcons))]
    public void Verifier_BrokenIcon_IsDroppedButCatalogIsAccepted(string icon)
    {
        using var kit = new DeliveryKit();
        var json = DeliveryKit.CatalogBytes([EntryWithIcon("alpha", icon), EntryWithIcon("beta", Convert.ToBase64String(Png()))]);

        var catalog = kit.Verifier.Verify(json, kit.Sign(json), allowFileUrls: false);

        var alpha = catalog.Modules.Single(m => m.Id == "alpha");
        Assert.Null(alpha.Icon);
        Assert.Equal("Тест", alpha.Name);
        Assert.Equal("https://example.com/demo.zip", alpha.Url);
        Assert.NotNull(catalog.Modules.Single(m => m.Id == "beta").Icon);
        Assert.Single(catalog.IconProblems);
        Assert.Contains("'alpha'", catalog.IconProblems[0]);
    }

    [Fact]
    public async Task CatalogService_BrokenIcon_WritesWarnAndKeepsTheRestOfCatalog()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(EntryWithIcon("alpha", "це не base64!"));
        var service = new CatalogService(new FileCatalogSource(dir, kit.Verifier), kit.Verifier, kit.Paths, kit.Log);

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Ok, result.Status);
        Assert.Null(service.Current!.Modules.Single().Icon);
        Assert.Contains(kit.Log.Messages, m => m.StartsWith("warn:") && m.Contains("'alpha'") && m.Contains("значок"));
    }

    [Fact]
    public void IconField_IsIgnoredByModelWithoutIt()
    {
        // Старий лаунчер не знає поля icon: JSON каталогу з ним має читатися без помилок і без втрати решти полів.
        var json = DeliveryKit.CatalogBytes([EntryWithIcon("alpha", Convert.ToBase64String(Png()))]);
        Assert.Contains("\"icon\"", System.Text.Encoding.UTF8.GetString(json));

        var old = JsonSerializer.Deserialize<OldCatalog>(json, KastomJson.Options)!;

        Assert.Equal("alpha", old.Modules.Single().Id);
        Assert.Equal("https://example.com/demo.zip", old.Modules.Single().Url);
    }

    [Fact]
    public void CatalogWithoutIconField_IsReadAsBefore()
    {
        using var kit = new DeliveryKit();
        const string Json = """{"schemaVersion":1,"generatedAt":"2026-10-01T12:00:00Z","modules":[{"id":"alpha","name":"Альфа","version":"1.0.0","contractVersion":1,"url":"https://example.com/a.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":10}]}""";
        var bytes = System.Text.Encoding.UTF8.GetBytes(Json);

        var catalog = kit.Verifier.Verify(bytes, kit.Sign(bytes), allowFileUrls: false);

        Assert.Null(catalog.Modules.Single().Icon);
        Assert.Empty(catalog.IconProblems);
    }

    [Fact]
    public void IconProblems_AreNotWrittenToCatalogJson()
    {
        var catalog = new ModuleCatalog { Modules = [], IconProblems = ["щось"] };

        var json = JsonSerializer.Serialize(catalog, KastomJson.Options);

        Assert.DoesNotContain("iconProblems", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OldCatalog
    {
        public List<OldEntry> Modules { get; init; } = [];
    }

    private sealed class OldEntry
    {
        public string Id { get; init; } = "";

        public string Url { get; init; } = "";
    }

    // ---- 3. Установник ----

    private static ModuleInstaller CreateInstaller(DeliveryKit kit) =>
        new(new HttpClient(), kit.Paths, kit.Log, Launcher, allowFileUrls: true);

    [Fact]
    public async Task Install_IconInPackage_IsExtracted()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"), ("icon.png", Png()));

        var result = await CreateInstaller(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("icon.png", result.Manifest!.Icon);
        Assert.Equal(Png(), File.ReadAllBytes(Path.Combine(kit.Paths.ModuleInstallDirectory("demo", "1.0.0"), "icon.png")));
    }

    [Fact]
    public async Task Install_IconFileMissingInPackage_StillInstalls()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"));

        var result = await CreateInstaller(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(Directory.Exists(kit.Paths.ModuleInstallDirectory("demo", "1.0.0")));
    }

    [Theory]
    [InlineData("../icon.png")]
    [InlineData("assets/../../icon.png")]
    [InlineData("/icon.png")]
    [InlineData("\\icon.png")]
    [InlineData("C:/Windows/icon.png")]
    [InlineData("C:\\Windows\\icon.png")]
    public async Task Install_UnsafeIconPath_IsRefused(string icon)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", icon));

        var result = await CreateInstaller(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("значка", result.Error);
        Assert.False(Directory.Exists(kit.Paths.ModuleInstallDirectory("demo", "1.0.0")));
    }

    // ---- 4. CatalogTool (запускається зібраний kastom-catalog.exe) ----

    [Fact]
    public void CatalogTool_PackageWithIcon_PutsBase64IconInCatalogAndSignatureCoversIt()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        var png = Png(500);
        kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "assets/icon.png"), ("assets/icon.png", png));
        kit.BuildZip("plain", "1.0.0");

        var result = RunCatalog(tool, kit);

        Assert.Equal(0, result.ExitCode);
        var catalog = ReadCatalog(OutCatalog(kit));
        Assert.Equal(png, Convert.FromBase64String(catalog.Modules.Single(m => m.Id == "demo").Icon!));
        Assert.Null(catalog.Modules.Single(m => m.Id == "plain").Icon);

        // Підпис покриває точні байти, значок усередині: лаунчер приймає пару й бачить значок, а зміна значка ламає підпис.
        var bytes = File.ReadAllBytes(OutCatalog(kit));
        var verified = kit.Verifier.Verify(bytes, kit.Sign(bytes), allowFileUrls: false);
        Assert.Equal(png, Convert.FromBase64String(verified.Modules.Single(m => m.Id == "demo").Icon!));
        // JSON екранує "+" і "/" з base64, тож шукається рядок у тому вигляді, в якому він лежить у файлі.
        string InFile(byte[] data) => JsonSerializer.Serialize(Convert.ToBase64String(data))[1..^1];
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(InFile(png), text);
        var tampered = System.Text.Encoding.UTF8.GetBytes(text.Replace(InFile(png), InFile(Png(500).Reverse().ToArray())));
        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(tampered, kit.Sign(bytes), allowFileUrls: false));
    }

    [Fact]
    public void CatalogTool_PackageWithoutIcon_HasNoIconField()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        kit.BuildZip("plain", "1.0.0");

        Assert.Equal(0, RunCatalog(tool, kit).ExitCode);

        Assert.Null(ReadCatalog(OutCatalog(kit)).Modules.Single().Icon);
    }

    public static TheoryData<string, string> BrokenPackages() => new()
    {
        { "not-png", "PNG" },
        { "too-big", "завеликий" },
        { "empty", "порожній" },
        { "missing", "немає в архіві" },
        { "path", "'..'" },
    };

    [Theory]
    [MemberData(nameof(BrokenPackages))]
    public void CatalogTool_BrokenIcon_FailsWithExplanation(string kind, string expectedInMessage)
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        switch (kind)
        {
            case "not-png":
                kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"), ("icon.png", "GIF89a....."u8.ToArray()));
                break;
            case "too-big":
                kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"), ("icon.png", Png(ModuleIcons.MaxSize + 1)));
                break;
            case "empty":
                kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"), ("icon.png", []));
                break;
            case "missing":
                kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "icon.png"));
                break;
            default:
                kit.BuildZip("demo", "1.0.0", ManifestWithIcon("demo", "1.0.0", "../icon.png"));
                break;
        }

        var result = RunCatalog(tool, kit);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedInMessage, result.Error);
        Assert.Contains("demo-1.0.0.zip", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_KeepsIconsOfEntriesThatAreNotReplaced()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        var oldIcon = Convert.ToBase64String(Png(300));
        var replacedIcon = Convert.ToBase64String(Png(200));
        var baseFile = WriteSignedBase(kit,
            EntryWithIcon("kept", oldIcon, "1.0.0"),
            EntryWithIcon("replaced", replacedIcon, "1.0.0"));
        var newPng = Png(400);
        kit.BuildZip("replaced", "2.0.0", ManifestWithIcon("replaced", "2.0.0", "icon.png"), ("icon.png", newPng));

        var result = RunCatalog(tool, kit, "--base", baseFile, "--public", kit.PublicKey);

        Assert.Equal(0, result.ExitCode);
        var catalog = ReadCatalog(OutCatalog(kit));
        Assert.Equal(oldIcon, catalog.Modules.Single(m => m.Id == "kept").Icon);
        Assert.Equal(Convert.ToBase64String(newPng), catalog.Modules.Single(m => m.Id == "replaced").Icon);
    }

    [Fact]
    public void CatalogTool_Base_ReplacedModuleWithoutIcon_LosesOldIcon()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        var baseFile = WriteSignedBase(kit, EntryWithIcon("demo", Convert.ToBase64String(Png(300)), "1.0.0"));
        kit.BuildZip("demo", "2.0.0");

        var result = RunCatalog(tool, kit, "--base", baseFile, "--public", kit.PublicKey);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(ReadCatalog(OutCatalog(kit)).Modules.Single().Icon);
    }

    [Fact]
    public void CatalogTool_Base_BrokenIconInBase_IsDroppedWithWarning()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        var baseFile = WriteSignedBase(kit,
            EntryWithIcon("kept", "це не base64!", "1.0.0"),
            EntryWithIcon("good", Convert.ToBase64String(Png()), "1.0.0"));
        kit.BuildZip("other", "1.0.0");

        var result = RunCatalog(tool, kit, "--base", baseFile, "--public", kit.PublicKey);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("'kept'", result.Output);
        var catalog = ReadCatalog(OutCatalog(kit));
        Assert.Null(catalog.Modules.Single(m => m.Id == "kept").Icon);
        Assert.NotNull(catalog.Modules.Single(m => m.Id == "good").Icon);
    }

    [Fact]
    public void CatalogTool_Pack_ChecksIcon()
    {
        var tool = FindTool();
        using var kit = new DeliveryKit();
        var dir = Path.Combine(kit.Root, "build");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "module.json"), ManifestWithIcon("demo", "1.0.0", "icon.png"));
        File.WriteAllBytes(Path.Combine(dir, "mod.dll"), [1, 2, 3]);
        var zip = Path.Combine(kit.Root, "out", "demo.zip");

        var missing = RunCapture(tool, "pack", "--dir", dir, "--out", zip);
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("значка", missing.Error);

        File.WriteAllBytes(Path.Combine(dir, "icon.png"), "GIF89a....."u8.ToArray());
        Assert.Equal(1, RunCapture(tool, "pack", "--dir", dir, "--out", zip).ExitCode);

        File.WriteAllBytes(Path.Combine(dir, "icon.png"), Png());
        Assert.Equal(0, RunCapture(tool, "pack", "--dir", dir, "--out", zip).ExitCode);
        Assert.True(File.Exists(zip));
    }

    // ---- допоміжне ----

    private static CatalogEntry EntryWithIcon(string id, string icon, string version) => new()
    {
        Id = id,
        Name = "Тест " + id,
        Version = version,
        ContractVersion = 1,
        Url = "https://old.example/" + id + "-" + version + ".zip",
        Sha256 = new string('a', 64),
        Size = 10,
        Icon = icon,
    };

    private static string WriteSignedBase(DeliveryKit kit, params CatalogEntry[] entries)
    {
        var path = Path.Combine(kit.Root, "base.json");
        File.WriteAllBytes(path, DeliveryKit.CatalogBytes(entries, generatedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
        File.WriteAllText(path + ".sig", kit.Sign(File.ReadAllBytes(path)));
        return path;
    }

    private static string OutCatalog(DeliveryKit kit) => Path.Combine(kit.Root, "out", "catalog.json");

    private static ModuleCatalog ReadCatalog(string path) =>
        JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllBytes(path), KastomJson.Options)!;

    private static (int ExitCode, string Output, string Error) RunCatalog(string tool, DeliveryKit kit, params string[] extra)
    {
        var args = new List<string> { "catalog", "--packages", kit.PackagesDir, "--base-url", "https://example.com/p", "--out", OutCatalog(kit) };
        args.AddRange(extra);
        return RunCapture(tool, args.ToArray());
    }

    /// <summary>Конфігурація, з якою зібрано сам тестовий проєкт: інструмент береться з тієї ж, щоб не запустити застарілий.</summary>
    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static string FindTool()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Kastom.slnx"))) continue;
            var exe = Path.Combine(dir.FullName, "tools", "Kastom.CatalogTool", "bin", BuildConfiguration, "net10.0-windows", "kastom-catalog.exe");
            if (File.Exists(exe)) return exe;
            Assert.Fail($"Не знайдено {exe}. Зберіть інструмент: dotnet build tools\\Kastom.CatalogTool -c {BuildConfiguration} (або dotnet build Kastom.slnx -c {BuildConfiguration}).");
        }

        Assert.Fail("Не знайдено теку репозиторію з Kastom.slnx, тож kastom-catalog.exe не знайти.");
        return "";
    }

    private static (int ExitCode, string Output, string Error) RunCapture(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        // Обидва потоки читаються паралельно: інакше повний буфер одного з них блокує процес.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }
}
