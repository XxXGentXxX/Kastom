using System.Net.Http;
using System.IO;
using Kastom.Core.Installation;
using Kastom.Core.Models;

namespace Kastom.Core.Tests.Delivery;

public class ModuleInstallerTests
{
    private static readonly ModuleVersion Launcher = ModuleVersion.Parse("1.0.0");

    private static ModuleInstaller Create(DeliveryKit kit, bool allowFileUrls = true) =>
        new(new HttpClient(new FailingHandler()), kit.Paths, kit.Log, Launcher, allowFileUrls);

    [Fact]
    public async Task Install_ViaFileUrl_PlacesModuleAndCleansUp()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", null, ("sub/readme.txt", [9, 9]));
        var progress = new CollectingProgress();

        var result = await Create(kit).InstallAsync(kit.Entry(zip), progress, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("demo", result.Manifest!.Id);
        var dir = kit.Paths.ModuleInstallDirectory("demo", "1.0.0");
        Assert.True(File.Exists(Path.Combine(dir, "mod.dll")));
        Assert.True(File.Exists(Path.Combine(dir, "sub", "readme.txt")));
        Assert.Empty(Directory.GetFiles(kit.Paths.DownloadsRoot));
        Assert.Empty(Directory.GetDirectories(Path.Combine(kit.Paths.ModulesRoot, "demo"), ".staging-*"));
        Assert.Equal(1.0, progress.Values[^1]);
    }

    [Fact]
    public async Task Install_FileUrlNotAllowed_Fails()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");

        var result = await Create(kit, allowFileUrls: false).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(kit.Paths.ModulesRoot));
    }

    [Fact]
    public async Task Install_Sha256Mismatch_FailsAndLeavesNothing()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");

        var result = await Create(kit).InstallAsync(kit.Entry(zip, sha256: new string('0', 64)), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Контрольна сума пакета не збігається", result.Error);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Install_SizeMismatch_Fails(int delta)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var size = new FileInfo(zip).Length + delta;

        var result = await Create(kit).InstallAsync(kit.Entry(zip, size: size), null, CancellationToken.None);

        Assert.False(result.Success);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("..\\evil.txt")]
    public async Task Install_ZipSlipEntry_RejectedWithNothingWrittenOutside(string badName)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", null, (badName, [66]));

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(kit.Root, "evil.txt", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.GetPathRoot(kit.Root)!, "evil.txt", SearchOption.TopDirectoryOnly));
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Fact]
    public async Task Install_TooManyEntries_Fails()
    {
        using var kit = new DeliveryKit();
        var extra = Enumerable.Range(0, 10_001).Select(i => ($"f{i}.txt", Array.Empty<byte>())).ToArray();
        var zip = kit.BuildZip("demo", "1.0.0", null, extra);

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Fact]
    public async Task Install_ManifestIdMismatch_Fails()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", DeliveryKit.ManifestJson("other", "1.0.0"));

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Fact]
    public async Task Install_ManifestVersionMismatch_Fails()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", DeliveryKit.ManifestJson("demo", "1.0.1"));

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Install_ManifestContractMismatch_Fails()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", DeliveryKit.ManifestJson("demo", "1.0.0", contract: 2));

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Theory]
    [InlineData("sub\\mod.dll")]
    [InlineData("../mod.dll")]
    [InlineData("missing.dll")]
    public async Task Install_BadEntryAssembly_Fails(string assembly)
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0", DeliveryKit.ManifestJson("demo", "1.0.0", entryAssembly: assembly));

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.False(result.Success);
        AssertNothingInstalled(kit, "demo", "1.0.0");
    }

    [Fact]
    public async Task Install_EntryContractDiffersFromLauncher_FailsBeforeDownload()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");

        var result = await Create(kit).InstallAsync(kit.Entry(zip, contract: 7), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("v7", result.Error);
        Assert.False(Directory.Exists(kit.Paths.DownloadsRoot));
    }

    [Fact]
    public async Task Install_LauncherTooOld_Fails()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");

        var result = await Create(kit).InstallAsync(kit.Entry(zip, minLauncher: "2.0.0"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("2.0.0", result.Error);
    }

    [Fact]
    public async Task Install_Twice_SecondTimeDoesNotDownload()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var entry = kit.Entry(zip);
        var installer = Create(kit);
        Assert.True((await installer.InstallAsync(entry, null, CancellationToken.None)).Success);

        File.Delete(zip);
        var second = await installer.InstallAsync(entry, null, CancellationToken.None);

        Assert.True(second.Success, second.Error);
        Assert.Equal("demo", second.Manifest!.Id);
    }

    [Fact]
    public async Task Install_BrokenExistingFolder_IsReplaced()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var dir = kit.Paths.ModuleInstallDirectory("demo", "1.0.0");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "junk.txt"), "x");

        var result = await Create(kit).InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Path.Combine(dir, "junk.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "mod.dll")));
    }

    [Fact]
    public async Task Install_Cancelled_Throws()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(kit).InstallAsync(kit.Entry(zip), null, cts.Token));
    }

    [Fact]
    public async Task Install_HttpsDownload_UsesHttpClient()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var bytes = File.ReadAllBytes(zip);
        var http = new HttpClient(new FuncHandler(_ => new HttpResponseMessage { Content = new ByteArrayContent(bytes) }));
        var installer = new ModuleInstaller(http, kit.Paths, kit.Log, Launcher, allowFileUrls: false);

        var result = await installer.InstallAsync(kit.Entry(zip, url: "https://example.com/demo.zip"), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task Install_HttpsHugeBody_StoppedBySizeCheck()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var http = new HttpClient(new FuncHandler(_ => new HttpResponseMessage { Content = new ByteArrayContent(new byte[100_000]) }));
        var installer = new ModuleInstaller(http, kit.Paths, kit.Log, Launcher, allowFileUrls: false);

        var result = await installer.InstallAsync(kit.Entry(zip, url: "https://example.com/demo.zip"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(kit.Paths.DownloadsRoot));
    }

    [Fact]
    public async Task ReadInstalledManifest_ReturnsManifestOrNull()
    {
        using var kit = new DeliveryKit();
        var zip = kit.BuildZip("demo", "1.0.0");
        var installer = Create(kit);
        await installer.InstallAsync(kit.Entry(zip), null, CancellationToken.None);

        Assert.Equal("demo", installer.ReadInstalledManifest("demo", "1.0.0")?.Id);
        Assert.Null(installer.ReadInstalledManifest("demo", "2.0.0"));
        Assert.Null(installer.ReadInstalledManifest("..", "1.0.0"));
        Assert.Null(installer.ReadInstalledManifest("demo", "..\\x"));
    }

    [Fact]
    public async Task RemoveVersion_RemovesFolder_AndIgnoresMissing()
    {
        using var kit = new DeliveryKit();
        var installer = Create(kit);
        await installer.InstallAsync(kit.Entry(kit.BuildZip("demo", "1.0.0")), null, CancellationToken.None);

        installer.RemoveVersion("demo", "1.0.0");
        installer.RemoveVersion("demo", "1.0.0");
        installer.RemoveModule("demo");
        installer.RemoveModule("nothing");

        Assert.False(Directory.Exists(kit.Paths.ModuleInstallDirectory("demo", "1.0.0")));
    }

    [Theory]
    [InlineData("demo", "..\\x")]
    [InlineData("demo", "../x")]
    [InlineData("demo", "..")]
    [InlineData("..", "1.0.0")]
    [InlineData("..\\..", "1.0.0")]
    [InlineData("a/b", "1.0.0")]
    public void RemoveVersion_RejectsTraversal(string id, string version)
    {
        using var kit = new DeliveryKit();
        var sentinel = Path.Combine(kit.Paths.LocalRoot, "keep.txt");
        Directory.CreateDirectory(kit.Paths.ModulesRoot);
        File.WriteAllText(sentinel, "x");

        Assert.Throws<ArgumentException>(() => Create(kit).RemoveVersion(id, version));
        Assert.True(File.Exists(sentinel));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\x")]
    [InlineData("")]
    public void RemoveModule_RejectsTraversal(string id)
    {
        using var kit = new DeliveryKit();

        Assert.Throws<ArgumentException>(() => Create(kit).RemoveModule(id));
    }

    private static void AssertNothingInstalled(DeliveryKit kit, string id, string version)
    {
        Assert.False(Directory.Exists(kit.Paths.ModuleInstallDirectory(id, version)));
        var moduleDir = Path.Combine(kit.Paths.ModulesRoot, id);
        if (Directory.Exists(moduleDir)) Assert.Empty(Directory.GetFileSystemEntries(moduleDir));
        if (Directory.Exists(kit.Paths.DownloadsRoot)) Assert.Empty(Directory.GetFiles(kit.Paths.DownloadsRoot));
    }

    private sealed class CollectingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }
}

/// <summary>Обробник, що провалює тест, якщо встановлювач усе ж звернувся до мережі.</summary>
internal sealed class FailingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Мережа в цьому тесті не потрібна: " + request.RequestUri);
}

internal sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
