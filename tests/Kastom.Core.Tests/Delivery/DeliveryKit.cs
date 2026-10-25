using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kastom.Core.Catalog;
using Kastom.Core.Models;
using Kastom.Sdk.Services;

namespace Kastom.Core.Tests.Delivery;

internal sealed class TestLogger : IModuleLogger
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add("info: " + message);

    public void Warn(string message) => Messages.Add("warn: " + message);

    public void Error(string message, Exception? exception = null) => Messages.Add("error: " + message);
}

/// <summary>Тимчасова папка тесту: ключ, пакети, підписаний каталог, корені KastomPaths. Прибирається в Dispose.</summary>
internal sealed class DeliveryKit : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public DeliveryKit()
    {
        Root = Path.Combine(Path.GetTempPath(), "kastom-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PackagesDir);
        Paths = new KastomPaths(Path.Combine(Root, "local"), Path.Combine(Root, "roaming"));
    }

    public string Root { get; }

    public string PackagesDir => Path.Combine(Root, "packages");

    public KastomPaths Paths { get; }

    public TestLogger Log { get; } = new();

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public CatalogVerifier Verifier => new(PublicKey);

    public static string ManifestJson(string id, string version, int contract = 1, string entryAssembly = "mod.dll", string entryType = "Demo.Module") =>
        JsonSerializer.Serialize(
            new { id, name = "Тест " + id, version, contractVersion = contract, entryAssembly, entryType },
            KastomJson.Options);

    /// <summary>Пакет із module.json і фіктивною mod.dll плюс додаткові записи (імена можуть бути небезпечними).</summary>
    public string BuildZip(string id, string version, string? manifestJson = null, params (string Name, byte[] Data)[] extra)
    {
        var path = Path.Combine(PackagesDir, $"{id}-{version}.zip");
        using (var stream = new FileStream(path, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(archive, "module.json", System.Text.Encoding.UTF8.GetBytes(manifestJson ?? ManifestJson(id, version)));
            Add(archive, "mod.dll", [1, 2, 3, 4, 5]);
            foreach (var (name, data) in extra) Add(archive, name, data);
        }

        return path;
    }

    public CatalogEntry Entry(string zipPath, string id = "demo", string version = "1.0.0", string? sha256 = null,
        long? size = null, string? url = null, int contract = 1, string minLauncher = "0.1.0")
    {
        var bytes = File.ReadAllBytes(zipPath);
        return new CatalogEntry
        {
            Id = id,
            Name = "Тест " + id,
            Version = version,
            ContractVersion = contract,
            MinLauncherVersion = minLauncher,
            Url = url ?? new Uri(zipPath).AbsoluteUri,
            Sha256 = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Size = size ?? bytes.Length,
        };
    }

    public static CatalogEntry PlainEntry(string id = "demo", string url = "https://example.com/demo.zip") => new()
    {
        Id = id,
        Name = "Тест",
        Version = "1.0.0",
        ContractVersion = 1,
        Url = url,
        Sha256 = new string('a', 64),
        Size = 10,
    };

    public static byte[] CatalogBytes(IEnumerable<CatalogEntry> entries, int schema = 1, DateTimeOffset? generatedAt = null, LauncherRelease? launcher = null) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new ModuleCatalog { SchemaVersion = schema, GeneratedAt = generatedAt ?? DateTimeOffset.UtcNow, Modules = entries.ToList(), Launcher = launcher },
            KastomJson.Options);

    public string Sign(byte[] data) => Convert.ToBase64String(_key.SignData(data, HashAlgorithmName.SHA256));

    public (byte[] Json, string Signature) SignedCatalog(params CatalogEntry[] entries)
    {
        var json = CatalogBytes(entries);
        return (json, Sign(json));
    }

    /// <summary>Пише catalog.json і catalog.json.sig у папку (за замовчуванням — власну папку каталогу тесту).</summary>
    public string WriteCatalog(params CatalogEntry[] entries)
    {
        var dir = Path.Combine(Root, "catalog");
        Directory.CreateDirectory(dir);
        var (json, signature) = SignedCatalog(entries);
        File.WriteAllBytes(Path.Combine(dir, ModuleCatalog.FileName), json);
        File.WriteAllText(Path.Combine(dir, ModuleCatalog.SignatureFileName), signature);
        return dir;
    }

    public void Dispose()
    {
        _key.Dispose();
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Add(ZipArchive archive, string name, byte[] data)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(data);
    }
}
