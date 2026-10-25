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

    // ---- 6. CatalogTool (запускається зібраний kastom-catalog.exe, якщо він є) ----

    [Fact]
    public void CatalogTool_LauncherOptions_WriteLauncherEntry()
    {
        var tool = FindTool();
        if (tool is null) return; // інструмент ще не зібрано: перевіряється при повній збірці рішення

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
        if (tool is null) return;

        using var kit = new DeliveryKit();
        kit.BuildZip("demo", "1.0.0");
        var catalogPath = Path.Combine(kit.Root, "out", "catalog.json");

        Assert.Equal(0, Run(tool, "catalog", "--packages", kit.PackagesDir, "--base-url", "https://example.com/p", "--out", catalogPath));
        var catalog = System.Text.Json.JsonSerializer.Deserialize<ModuleCatalog>(File.ReadAllBytes(catalogPath), KastomJson.Options)!;

        Assert.Null(catalog.Launcher);
    }

    private static string? FindTool()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Kastom.slnx"))) continue;
            foreach (var config in new[] { "Debug", "Release" })
            {
                var exe = Path.Combine(dir.FullName, "tools", "Kastom.CatalogTool", "bin", config, "net10.0-windows", "kastom-catalog.exe");
                if (File.Exists(exe)) return exe;
            }
        }

        return null;
    }

    private static int Run(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
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
