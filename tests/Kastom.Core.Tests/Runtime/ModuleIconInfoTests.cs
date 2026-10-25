using System.IO;
using System.Text.Json.Nodes;
using Kastom.Core.Catalog;
using Kastom.Core.Models;
using Kastom.Core.Tests.Delivery;

namespace Kastom.Core.Tests.Runtime;

/// <summary>ModuleInfo.Icon: значок із встановленого пакета, інакше з каталогу, інакше null.</summary>
public sealed class ModuleIconInfoTests : IDisposable
{
    private readonly ModuleManagerFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static byte[] Png(int length = 100) => ModuleIconTests.Png(length);

    /// <summary>Демо-модуль, чий module.json називає значок; сам файл кладеться, якщо data не null.</summary>
    private string AddDemoWithIcon(string? iconPath, byte[]? data, string version = "1.0.0")
    {
        _f.AddDemo(version);
        var directory = _f.Paths.ModuleInstallDirectory("demo", version);
        var manifestPath = Path.Combine(directory, ModuleManifest.FileName);
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        node["icon"] = iconPath;
        File.WriteAllText(manifestPath, node.ToJsonString());
        if (iconPath is not null && data is not null)
        {
            var file = Path.Combine(directory, iconPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        }

        return directory;
    }

    private void CatalogWithIcon(string? icon, string version = "1.0.0")
    {
        var entry = FakeCatalogService.Entry("demo", "Демо-модуль", version);
        _f.Catalog.Current = FakeCatalogService.Catalog(new CatalogEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Version = entry.Version,
            ContractVersion = entry.ContractVersion,
            Url = entry.Url,
            Sha256 = entry.Sha256,
            Icon = icon,
        });
    }

    [Fact]
    public void Installed_IconFromPackage()
    {
        var png = Png(321);
        AddDemoWithIcon("assets/icon.png", png);

        Assert.Equal(png, _f.Info("demo").Icon);
    }

    [Fact]
    public void Installed_PackageIconWinsOverCatalogIcon()
    {
        var fromPackage = Png(150);
        AddDemoWithIcon("icon.png", fromPackage);
        CatalogWithIcon(Convert.ToBase64String(Png(250)));

        Assert.Equal(fromPackage, _f.Info("demo").Icon);
    }

    [Fact]
    public void NotInstalled_IconFromCatalog()
    {
        var png = Png(222);
        CatalogWithIcon(Convert.ToBase64String(png));

        var info = _f.Info("demo");

        Assert.False(info.IsInstalled);
        Assert.Equal(png, info.Icon);
    }

    [Fact]
    public void Installed_ManifestWithoutIcon_FallsBackToCatalogIcon()
    {
        var png = Png(222);
        _f.AddDemo();
        CatalogWithIcon(Convert.ToBase64String(png));

        Assert.Equal(png, _f.Info("demo").Icon);
    }

    [Fact]
    public void Installed_IconFileMissing_FallsBackToCatalogIcon()
    {
        var png = Png(222);
        AddDemoWithIcon("icon.png", data: null);
        CatalogWithIcon(Convert.ToBase64String(png));

        Assert.Equal(png, _f.Info("demo").Icon);
    }

    [Fact]
    public void NoIconAnywhere_IsNull()
    {
        _f.AddDemo();
        CatalogWithIcon(null);

        Assert.Null(_f.Info("demo").Icon);
    }

    [Fact]
    public void NoIconAnywhere_ManifestNamesMissingFile_IsNull()
    {
        AddDemoWithIcon("icon.png", data: null);

        Assert.Null(_f.Info("demo").Icon);
    }

    [Fact]
    public void BrokenCatalogIcon_IsNullAndLogged()
    {
        // Каталог не з перевіряльника (підроблений в тесті) може нести що завгодно: менеджер не повинен падати.
        CatalogWithIcon("це не base64!");

        Assert.Null(_f.Info("demo").Icon);
        Assert.Contains(_f.Log.Lines, l => l.StartsWith("[WARN]") && l.Contains("'demo'") && l.Contains("значок"));
    }

    [Theory]
    [InlineData("not-png")]
    [InlineData("too-big")]
    [InlineData("empty")]
    public async Task Installed_BrokenIconFile_IsNull_AndModuleStillRuns(string kind)
    {
        var data = kind switch
        {
            "not-png" => "GIF89a....."u8.ToArray(),
            "too-big" => Png(ModuleIcons.MaxSize + 1),
            _ => Array.Empty<byte>(),
        };
        AddDemoWithIcon("icon.png", data);

        await _f.Manager.StartAsync(CancellationToken.None);

        var info = _f.Info("demo");
        Assert.Null(info.Icon);
        Assert.True(info.IsRunning);
        Assert.Null(info.Error);
    }

    [Theory]
    [InlineData("../icon.png")]
    [InlineData("/icon.png")]
    [InlineData("C:/icon.png")]
    public void Installed_UnsafeIconPath_IsNullAndNothingOutsideIsRead(string iconPath)
    {
        // Файл за межами папки модуля існує й був би правильним PNG: шлях має відсікатися до читання.
        var directory = AddDemoWithIcon("icon.png", data: null);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(directory)!, "icon.png"), Png());
        var manifestPath = Path.Combine(directory, ModuleManifest.FileName);
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        node["icon"] = iconPath;
        File.WriteAllText(manifestPath, node.ToJsonString());

        Assert.Null(_f.Info("demo").Icon);
    }

    [Fact]
    public void Installed_IconIsReadOnlyOncePerVersion()
    {
        var png = Png(180);
        var directory = AddDemoWithIcon("icon.png", png);

        var first = _f.Info("demo").Icon;
        File.Delete(Path.Combine(directory, "icon.png"));
        var second = _f.Info("demo").Icon;

        Assert.Equal(png, first);
        Assert.Same(first, second);
    }

    [Fact]
    public void CatalogIcon_IsDecodedOncePerCatalog()
    {
        CatalogWithIcon(Convert.ToBase64String(Png()));

        Assert.Same(_f.Info("demo").Icon, _f.Info("demo").Icon);
    }
}
