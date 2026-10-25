using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Kastom.Core.Abstractions;
using Kastom.Core.Catalog;
using Kastom.Core.Installation;
using Kastom.Core.Models;

namespace Kastom.Core.Tests.Delivery;

/// <summary>Захист від відкоту каталогу, таймаути читання, залишки staging, зарезервовані імена, реліз лаунчера.</summary>
public class HardeningTests
{
    private static readonly ModuleVersion Launcher = ModuleVersion.Parse("1.0.0");
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- 1. Анти-відкіт ----

    private static CatalogFetchResult Ok(DeliveryKit kit, byte[] json) => new()
    {
        Status = CatalogFetchStatus.Ok,
        Catalog = kit.Verifier.Verify(json, kit.Sign(json), allowFileUrls: false),
        RawCatalog = json,
        Signature = kit.Sign(json),
    };

    private static byte[] Catalog(string id, DateTimeOffset at) => DeliveryKit.CatalogBytes([DeliveryKit.PlainEntry(id)], generatedAt: at);

    [Fact]
    public async Task Service_OlderCatalog_IsRejected_KeepsCurrentAndCache()
    {
        using var kit = new DeliveryKit();
        var source = new QueueSource(Ok(kit, Catalog("alpha", T0)));
        var service = new CatalogService(source, kit.Verifier, kit.Paths, kit.Log);
        await service.RefreshAsync(CancellationToken.None);
        var cacheFile = Path.Combine(kit.Paths.CatalogCacheRoot, ModuleCatalog.FileName);
        var cached = File.ReadAllBytes(cacheFile);
        var raised = 0;
        service.CatalogChanged += (_, _) => raised++;

        source.Next = Ok(kit, Catalog("beta", T0.AddMinutes(-1)));
        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, result.Status);
        Assert.Contains("старіший каталог", result.Error);
        Assert.Equal("alpha", service.Current!.Modules.Single().Id);
        Assert.Equal(cached, File.ReadAllBytes(cacheFile));
        Assert.Equal(0, raised);
        Assert.Contains(kit.Log.Messages, m => m.StartsWith("warn:", StringComparison.Ordinal) && m.Contains("старіший"));
    }

    [Fact]
    public async Task Service_SameTimeDifferentBytes_IsRejected_SameBytesAccepted_NewerAccepted()
    {
        using var kit = new DeliveryKit();
        var source = new QueueSource(Ok(kit, Catalog("alpha", T0)));
        var service = new CatalogService(source, kit.Verifier, kit.Paths, kit.Log);
        await service.RefreshAsync(CancellationToken.None);

        source.Next = Ok(kit, Catalog("beta", T0));
        Assert.Equal(CatalogFetchStatus.Failed, (await service.RefreshAsync(CancellationToken.None)).Status);
        Assert.Equal("alpha", service.Current!.Modules.Single().Id);

        source.Next = Ok(kit, Catalog("alpha", T0));
        Assert.Equal(CatalogFetchStatus.Ok, (await service.RefreshAsync(CancellationToken.None)).Status);

        source.Next = Ok(kit, Catalog("beta", T0.AddMinutes(1)));
        Assert.Equal(CatalogFetchStatus.Ok, (await service.RefreshAsync(CancellationToken.None)).Status);
        Assert.Equal("beta", service.Current!.Modules.Single().Id);
    }

    [Fact]
    public async Task Service_CachedCatalogIsTheRollbackBaselineAfterRestart()
    {
        using var kit = new DeliveryKit();
        await new CatalogService(new QueueSource(Ok(kit, Catalog("alpha", T0))), kit.Verifier, kit.Paths, kit.Log)
            .RefreshAsync(CancellationToken.None);

        var restarted = new CatalogService(new QueueSource(Ok(kit, Catalog("old", T0.AddDays(-1)))), kit.Verifier, kit.Paths, kit.Log);
        var result = await restarted.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, result.Status);
        Assert.Equal("alpha", restarted.Current!.Modules.Single().Id);
    }

    // ---- 2. Таймаути ----

    [Fact]
    public async Task HttpSource_StalledBody_TimesOutAsFailed()
    {
        using var kit = new DeliveryKit();
        var handler = new FuncHandler(_ => new HttpResponseMessage { Content = new StallContent([[(byte)'{']]) });
        var source = new HttpCatalogSource(new HttpClient(handler), new Uri("https://example.com/catalog.json"), kit.Verifier)
        {
            FetchTimeout = TimeSpan.FromMilliseconds(300),
        };

        var result = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task HttpSource_CallerCancellationStillThrows()
    {
        using var kit = new DeliveryKit();
        var handler = new FuncHandler(_ => new HttpResponseMessage { Content = new StallContent([]) });
        var source = new HttpCatalogSource(new HttpClient(handler), new Uri("https://example.com/catalog.json"), kit.Verifier);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchAsync(cts.Token));
    }

    [Fact]
    public async Task Installer_StalledDownload_FailsInsteadOfThrowing_AndCleansUp()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var bytes = File.ReadAllBytes(zip);
        var http = new HttpClient(new FuncHandler(_ => new HttpResponseMessage { Content = new StallContent([bytes[..10]]) }));
        var installer = new ModuleInstaller(http, kit.Paths, kit.Log, Launcher, allowFileUrls: false) { IdleTimeout = TimeSpan.FromMilliseconds(300) };

        var result = await installer.InstallAsync(kit.Entry(zip, url: "https://example.com/demo.zip"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("не надходило даних", result.Error);
        Assert.Empty(Directory.GetFiles(kit.Paths.DownloadsRoot));
    }

    [Fact]
    public async Task Installer_SlowButSteadyDownload_IsNotCancelled()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var bytes = File.ReadAllBytes(zip);
        var parts = Enumerable.Range(0, 4).Select(i => bytes[(i * bytes.Length / 4)..((i + 1) * bytes.Length / 4)]).ToArray();
        var http = new HttpClient(new FuncHandler(_ => new HttpResponseMessage { Content = new SlowContent(parts, TimeSpan.FromMilliseconds(120)) }));
        var installer = new ModuleInstaller(http, kit.Paths, kit.Log, Launcher, allowFileUrls: false) { IdleTimeout = TimeSpan.FromMilliseconds(400) };

        var result = await installer.InstallAsync(kit.Entry(zip, url: "https://example.com/demo.zip"), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task Installer_CallerCancellationDuringStall_Throws()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var http = new HttpClient(new FuncHandler(_ => new HttpResponseMessage { Content = new StallContent([]) }));
        var installer = new ModuleInstaller(http, kit.Paths, kit.Log, Launcher, allowFileUrls: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.InstallAsync(kit.Entry(zip, url: "https://example.com/demo.zip"), null, cts.Token));
        Assert.Empty(Directory.GetFiles(kit.Paths.DownloadsRoot));
    }

    // ---- 3. Залишки staging ----

    [Fact]
    public async Task Installer_RemovesStaleStaging_KeepsInstalledVersions()
    {
        using var kit = new DeliveryKit();
        var stale = Path.Combine(kit.Paths.ModulesRoot, "old", ".staging-deadbeef");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "x.dll"), "x");
        var kept = Path.Combine(kit.Paths.ModulesRoot, "old", "1.0.0");
        Directory.CreateDirectory(kept);
        var zip = kit.BuildZip("demo", "1.0.0");
        var installer = new ModuleInstaller(new HttpClient(new FailingHandler()), kit.Paths, kit.Log, Launcher, allowFileUrls: true);

        var result = await installer.InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(kept));
    }

    // ---- 4. Зарезервовані імена ----

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("sub/NUL.dll")]
    [InlineData("sub\\Aux")]
    [InlineData("COM1")]
    [InlineData("lpt9.x.y")]
    [InlineData("PRN.")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("dir /a.txt")]
    [InlineData("CON /a.txt")]
    public async Task Install_ReservedOrTrailingDotSpaceName_Rejected(string name)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", null, (name, [1]));

        var result = await new ModuleInstaller(new HttpClient(new FailingHandler()), kit.Paths, kit.Log, Launcher, allowFileUrls: true)
            .InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(kit.Paths.ModuleInstallDirectory("demo", "1.0.0")));
    }

    [Theory]
    [InlineData("console.txt")]
    [InlineData("com10.txt")]
    [InlineData("icons/lptx.dll")]
    [InlineData("data/.hidden")]
    public async Task Install_SimilarButLegalNames_Accepted(string name)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", null, (name, [1]));

        var result = await new ModuleInstaller(new HttpClient(new FailingHandler()), kit.Paths, kit.Log, Launcher, allowFileUrls: true)
            .InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
    }

    // ---- 5. Реліз лаунчера у верифікаторі ----

    private static CatalogVerifier VerifierOf(DeliveryKit kit) => kit.Verifier;

    private static LauncherRelease Release(string version = "0.2.0", string? sha = null, string file = "Kastom-0.2.0-full.nupkg") =>
        new() { Version = version, Sha256 = sha ?? new string('b', 64), FileName = file };

    [Fact]
    public void Verify_ValidLauncherRelease_Accepted()
    {
        using var kit = new DeliveryKit();
        var json = DeliveryKit.CatalogBytes([], launcher: Release());

        var catalog = VerifierOf(kit).Verify(json, kit.Sign(json), false);

        Assert.Equal("0.2.0", catalog.Launcher!.Version);
        Assert.Equal("Kastom-0.2.0-full.nupkg", catalog.Launcher.FileName);
    }

    [Fact]
    public void Verify_CatalogWithoutLauncher_Accepted()
    {
        using var kit = new DeliveryKit();
        var json = DeliveryKit.CatalogBytes([]);

        Assert.Null(VerifierOf(kit).Verify(json, kit.Sign(json), false).Launcher);
    }

    [Theory]
    [InlineData("0.2", "x", "Kastom-0.2.0-full.nupkg")]
    [InlineData("скоро", null, "Kastom-0.2.0-full.nupkg")]
    [InlineData("1.0.0-..\\x", null, "Kastom-0.2.0-full.nupkg")]
    [InlineData("0.2.0", "ABCDEF", "Kastom-0.2.0-full.nupkg")]
    [InlineData("0.2.0", null, "..\\Kastom-0.2.0-full.nupkg")]
    [InlineData("0.2.0", null, "sub/Kastom-0.2.0-full.nupkg")]
    [InlineData("0.2.0", null, "Kastom.exe")]
    [InlineData("0.2.0", null, "")]
    public void Verify_BadLauncherRelease_Throws(string version, string? sha, string file)
    {
        using var kit = new DeliveryKit();
        var json = DeliveryKit.CatalogBytes([], launcher: Release(version, sha, file));

        Assert.Throws<CatalogValidationException>(() => VerifierOf(kit).Verify(json, kit.Sign(json), false));
    }

    // ---- 6. CatalogTool (запускається зібраний kastom-catalog.exe тієї ж конфігурації, що й тести) ----

    [Fact]
    public void CatalogTool_LauncherOptions_WriteLauncherEntry()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var package = Path.Combine(kit.Root, "Kastom-0.2.0-full.nupkg");
        File.WriteAllBytes(package, [1, 2, 3, 4]);
        var catalogPath = Path.Combine(kit.Root, "out", "catalog.json");

        var ok = Run(tool, "catalog", "--packages", kit.PackagesDir, "--base-url", "https://example.com/p", "--out", catalogPath,
            "--launcher-package", package, "--launcher-version", "0.2.0");
        var catalog = System.Text.Json.JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllBytes(catalogPath), KastomJson.Options)!;

        Assert.Equal(0, ok);
        Assert.Equal("0.2.0", catalog.Launcher!.Version);
        Assert.Equal("Kastom-0.2.0-full.nupkg", catalog.Launcher.FileName);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4])), catalog.Launcher.Sha256);
        Assert.Equal(2, Run(tool, "catalog", "--packages", kit.PackagesDir, "--base-url", "https://example.com/p",
            "--out", catalogPath, "--launcher-version", "0.2.0"));
        Assert.Single(catalog.Modules);
        Assert.NotEmpty(zip);
    }

    [Fact]
    public void CatalogTool_WithoutLauncherOptions_HasNoLauncherEntry()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "1.0.0");
        var catalogPath = Path.Combine(kit.Root, "out", "catalog.json");

        Assert.Equal(0, Run(tool, "catalog", "--packages", kit.PackagesDir, "--base-url", "https://example.com/p", "--out", catalogPath));
        var catalog = System.Text.Json.JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllBytes(catalogPath), KastomJson.Options)!;

        Assert.Null(catalog.Launcher);
    }

    // ---- 7. CatalogTool: sign, --base, verify без --public ----

    private static CatalogEntry BaseEntry(string id, string version, string url) => new()
    {
        Id = id,
        Name = "Тест " + id,
        Version = version,
        ContractVersion = 1,
        Url = url,
        Sha256 = new string('a', 64),
        Size = 10,
    };

    private static string WriteCatalogFile(DeliveryKit kit, string name, DateTimeOffset generatedAt, LauncherRelease? launcher, params CatalogEntry[] entries)
    {
        var path = Path.Combine(kit.Root, name);
        File.WriteAllBytes(path, DeliveryKit.CatalogBytes(entries, generatedAt: generatedAt, launcher: launcher));
        return path;
    }

    /// <summary>Базовий каталог з підписом ключа тесту (<see cref="DeliveryKit.PublicKey"/>).</summary>
    private static string WriteSignedBase(DeliveryKit kit, DateTimeOffset generatedAt, LauncherRelease? launcher, params CatalogEntry[] entries)
    {
        var path = WriteCatalogFile(kit, "base.json", generatedAt, launcher, entries);
        File.WriteAllText(path + ".sig", kit.Sign(File.ReadAllBytes(path)));
        return path;
    }

    private static string WriteSignedRaw(DeliveryKit kit, string json)
    {
        var path = Path.Combine(kit.Root, "base.json");
        File.WriteAllText(path, json);
        File.WriteAllText(path + ".sig", kit.Sign(File.ReadAllBytes(path)));
        return path;
    }

    private static string OutCatalog(DeliveryKit kit) => Path.Combine(kit.Root, "out", "catalog.json");

    private static (int ExitCode, string Output, string Error) RunCatalog(string tool, DeliveryKit kit, string baseFile, params string[] extra)
    {
        var args = new List<string>
        {
            "catalog", "--base", baseFile, "--public", kit.PublicKey, "--packages", kit.PackagesDir, "--base-url", "https://example.com/p",
            "--out", OutCatalog(kit),
        };
        args.AddRange(extra);
        return RunCapture(tool, args.ToArray());
    }

    private static string NewKey(string tool, DeliveryKit kit)
    {
        var privatePath = Path.Combine(kit.Root, "keys", "private.pem");
        Assert.Equal(0, Run(tool, "keygen", "--private", privatePath, "--public", Path.Combine(kit.Root, "keys", "public.txt")));
        return privatePath;
    }

    private static ModuleCatalog ReadCatalog(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllBytes(path), KastomJson.Options)!;

    [Fact]
    public void CatalogTool_Sign_RefusesCatalogDatedInTheFuture()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        var key = NewKey(tool, kit);
        var future = WriteCatalogFile(kit, "future.json", DateTimeOffset.UtcNow.AddHours(1), null);
        var slightlyAhead = WriteCatalogFile(kit, "ahead.json", DateTimeOffset.UtcNow.AddMinutes(2), null);
        var normal = WriteCatalogFile(kit, "normal.json", DateTimeOffset.UtcNow.AddMinutes(-1), null);
        var broken = Path.Combine(kit.Root, "broken.json");
        File.WriteAllText(broken, "це не json");

        var refused = RunCapture(tool, "sign", "--key", key, "--catalog", future);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("майбутнім", refused.Error);
        Assert.False(File.Exists(future + ".sig"));
        Assert.Equal(1, RunCapture(tool, "sign", "--key", key, "--catalog", broken).ExitCode);
        Assert.False(File.Exists(broken + ".sig"));
        Assert.Equal(0, Run(tool, "sign", "--key", key, "--catalog", slightlyAhead));
        Assert.Equal(0, Run(tool, "sign", "--key", key, "--catalog", normal));
        Assert.True(File.Exists(normal + ".sig"));
    }

    [Fact]
    public void CatalogTool_Base_MergesNewPackageAndKeepsOtherModules()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var baseRelease = new LauncherRelease { Version = "0.2.0", Sha256 = new string('b', 64), FileName = "Kastom-0.2.0-full.nupkg" };
        var baseFile = WriteSignedBase(kit, baseTime, baseRelease,
            BaseEntry("demo", "1.0.0", "https://old.example/demo-1.0.0.zip"),
            BaseEntry("other", "1.5.0", "https://old.example/other-1.5.0.zip"));

        var merged = RunCatalog(tool, kit, baseFile);
        var catalog = ReadCatalog(OutCatalog(kit));

        Assert.Equal(0, merged.ExitCode);
        Assert.Equal(["demo", "other"], catalog.Modules.Select(m => m.Id));
        var demo = catalog.Modules.Single(m => m.Id == "demo");
        Assert.Equal("2.0.0", demo.Version);
        Assert.Equal("https://example.com/p/demo-2.0.0.zip", demo.Url);
        var other = catalog.Modules.Single(m => m.Id == "other");
        Assert.Equal("1.5.0", other.Version);
        Assert.Equal("https://old.example/other-1.5.0.zip", other.Url);
        Assert.Equal("Kastom-0.2.0-full.nupkg", catalog.Launcher!.FileName);
        Assert.True(catalog.GeneratedAt > baseTime);

        // --launcher-* замінює реліз лаунчера з бази.
        var package = Path.Combine(kit.Root, "Kastom-0.3.0-full.nupkg");
        File.WriteAllBytes(package, [1, 2, 3]);
        var replaced = RunCatalog(tool, kit, baseFile, "--launcher-package", package, "--launcher-version", "0.3.0");
        Assert.Equal(0, replaced.ExitCode);
        Assert.Equal("0.3.0", ReadCatalog(OutCatalog(kit)).Launcher!.Version);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0")]
    public void CatalogTool_Base_SameOrNewerVersionInBase_Fails(string versionInBase)
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "1.0.0");
        var baseFile = WriteSignedBase(kit, DateTimeOffset.UtcNow.AddMinutes(-10), null,
            BaseEntry("demo", versionInBase, "https://old.example/demo.zip"));

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("не новіша", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_DatedInTheFuture_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteSignedBase(kit, DateTimeOffset.UtcNow.AddHours(1), null,
            BaseEntry("demo", "1.0.0", "https://old.example/demo.zip"));

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("старіший", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_InvalidEntry_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteSignedBase(kit, DateTimeOffset.UtcNow.AddMinutes(-10), null,
            BaseEntry("other", "1.0.0", "http://old.example/other.zip"));

        Assert.Equal(1, RunCatalog(tool, kit, baseFile).ExitCode);
    }

    [Fact]
    public void CatalogTool_Base_WithoutSignature_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteCatalogFile(kit, "base.json", DateTimeOffset.UtcNow.AddMinutes(-10), null,
            BaseEntry("other", "1.0.0", "https://old.example/other.zip"));

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("підпис", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_SignedByForeignKey_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        using var stranger = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteCatalogFile(kit, "base.json", DateTimeOffset.UtcNow.AddMinutes(-10), null,
            BaseEntry("other", "1.0.0", "https://old.example/other.zip"));
        File.WriteAllText(baseFile + ".sig", stranger.Sign(File.ReadAllBytes(baseFile)));

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("не збігається", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"generatedAt\":\"2026-10-01T12:00:00+00:00\"}")]
    [InlineData("{\"schemaVersion\":1,\"modules\":[{\"id\":\"other\",\"name\":\"T\",\"version\":\"1.0.0\",\"contractVersion\":1,\"url\":\"https://old.example/o.zip\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"size\":10}]}")]
    public void CatalogTool_Base_WithoutModulesOrGeneratedAt_Fails(string json)
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteSignedRaw(kit, json);

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    /// <summary>Відкликали останній модуль: підписана база з порожнім списком — не глухий кут для наступної публікації.</summary>
    [Fact]
    public void CatalogTool_Base_SignedEmptyModules_Succeeds()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteSignedRaw(kit, "{\"schemaVersion\":1,\"generatedAt\":\"2026-10-01T12:00:00+00:00\",\"modules\":[]}");

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"demo\"", File.ReadAllText(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_FileUrlWithHttpsBaseUrl_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");
        var baseFile = WriteSignedBase(kit, DateTimeOffset.UtcNow.AddMinutes(-10), null,
            BaseEntry("other", "1.0.0", "file:///C:/pkg/other.zip"));

        var result = RunCatalog(tool, kit, baseFile);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("https://", result.Error);
        Assert.False(File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Base_MissingFile_Fails()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "2.0.0");

        var result = RunCatalog(tool, kit, Path.Combine(kit.Root, "nope.json"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("не знайдено", result.Error);
    }

    [Theory]
    [InlineData("0.1.0", true, false)] // нижча версія
    [InlineData("0.2.0", false, false)] // та сама версія, інший SHA-256
    [InlineData("0.2.0", true, true)] // та сама версія, той самий SHA-256
    [InlineData("0.3.0", false, true)] // вища версія
    public void CatalogTool_Base_LauncherVersionRules(string requestedVersion, bool sameSha, bool accepted)
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        var package = Path.Combine(kit.Root, "Kastom-" + requestedVersion + "-full.nupkg");
        byte[] bytes = [1, 2, 3];
        File.WriteAllBytes(package, bytes);
        var baseSha = sameSha ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) : new string('c', 64);
        var baseRelease = new LauncherRelease { Version = "0.2.0", Sha256 = baseSha, FileName = "Kastom-0.2.0-full.nupkg" };
        var baseFile = WriteSignedBase(kit, DateTimeOffset.UtcNow.AddMinutes(-10), baseRelease,
            BaseEntry("other", "1.0.0", "https://old.example/other.zip"));

        var result = RunCatalog(tool, kit, baseFile, "--launcher-package", package, "--launcher-version", requestedVersion);

        Assert.Equal(accepted ? 0 : 1, result.ExitCode);
        Assert.Equal(accepted, File.Exists(OutCatalog(kit)));
    }

    [Fact]
    public void CatalogTool_Verify_WithoutPublic_UsesEmbeddedKey()
    {
        var tool = FindTool();

        using var kit = new DeliveryKit();
        var key = NewKey(tool, kit);
        var publicKey = Path.Combine(kit.Root, "keys", "public.txt");
        var catalog = WriteCatalogFile(kit, "catalog.json", DateTimeOffset.UtcNow.AddMinutes(-1), null);
        Assert.Equal(0, Run(tool, "sign", "--key", key, "--catalog", catalog));

        Assert.Equal(0, Run(tool, "verify", "--public", publicKey, "--catalog", catalog));
        // Без --public береться вшитий ключ: каталог, підписаний тимчасовим ключем, його не проходить, але це не помилка аргументів.
        var result = RunCapture(tool, "verify", "--catalog", catalog);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("не збігається", result.Error);
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

    private static int Run(string exe, params string[] args) => RunCapture(exe, args).ExitCode;

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

    // ---- допоміжні типи ----

    private sealed class QueueSource(CatalogFetchResult first) : ICatalogSource
    {
        public CatalogFetchResult Next { get; set; } = first;

        public bool AllowsFileUrls => false;

        public Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(Next);
    }

    /// <summary>Віддає задані шматки, а потім мовчить, доки не скасують читання.</summary>
    private sealed class StallContent(byte[][] chunks) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ChunkStream(chunks, TimeSpan.Zero, stallAtEnd: true));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>Віддає шматки з паузою між ними й нормально закінчується.</summary>
    private sealed class SlowContent(byte[][] chunks, TimeSpan delay) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ChunkStream(chunks, delay, stallAtEnd: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ChunkStream(byte[][] chunks, TimeSpan delay, bool stallAtEnd) : Stream
    {
        private int _index;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_index >= chunks.Length)
            {
                if (stallAtEnd) await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            if (delay > TimeSpan.Zero && _index > 0) await Task.Delay(delay, cancellationToken);
            var chunk = chunks[_index++];
            chunk.CopyTo(buffer);
            return chunk.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
