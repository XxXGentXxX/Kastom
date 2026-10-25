using System.Security.Cryptography;
using Kastom.Core.Catalog;
using Kastom.Core.Installation;

namespace Kastom.Core.Tests.Delivery;

public class CatalogVerifierTests
{
    [Fact]
    public void Verify_ValidCatalog_ReturnsCatalog()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry("alpha"), DeliveryKit.PlainEntry("beta"));

        var catalog = kit.Verifier.Verify(json, signature, allowFileUrls: false);

        Assert.Equal(["alpha", "beta"], catalog.Modules.Select(m => m.Id));
    }

    [Fact]
    public void Verify_TamperedByte_Throws()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry());
        json[json.Length / 2] ^= 1;

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_WrongKey_Throws()
    {
        using var kit = new DeliveryKit();
        using var other = new DeliveryKit();
        var (json, signature) = other.SignedCatalog(DeliveryKit.PlainEntry());

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_InvalidJsonWithValidSignature_Throws()
    {
        using var kit = new DeliveryKit();
        var json = "{ not json"u8.ToArray();

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, kit.Sign(json), false));
    }

    [Fact]
    public void Verify_GarbageSignature_Throws()
    {
        using var kit = new DeliveryKit();
        var (json, _) = kit.SignedCatalog(DeliveryKit.PlainEntry());

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, "!!не base64!!", false));
        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, Convert.ToBase64String(new byte[10]), false));
    }

    [Fact]
    public void Verify_HugeCatalog_Throws()
    {
        using var kit = new DeliveryKit();
        var json = new byte[CatalogVerifier.MaxCatalogSize + 1];

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, kit.Sign(json), false));
    }

    [Fact]
    public void Verify_HttpUrl_Rejected()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry(url: "http://example.com/demo.zip"));

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, allowFileUrls: true));
    }

    [Fact]
    public void Verify_FileUrl_RejectedUnlessAllowed()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry(url: "file:///C:/pkg/demo.zip"));

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, allowFileUrls: false));
        Assert.Single(kit.Verifier.Verify(json, signature, allowFileUrls: true).Modules);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Verify_BadSha256_Throws(string sha)
    {
        using var kit = new DeliveryKit();
        var entry = new Kastom.Core.Models.CatalogEntry
        {
            Id = "demo", Name = "Тест", Version = "1.0.0", ContractVersion = 1, Url = "https://example.com/d.zip", Sha256 = sha,
        };
        var (json, signature) = kit.SignedCatalog(entry);

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_DuplicateIds_Throws()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry("same"), DeliveryKit.PlainEntry("same"));

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_WrongSchemaVersion_Throws()
    {
        using var kit = new DeliveryKit();
        var json = DeliveryKit.CatalogBytes([DeliveryKit.PlainEntry()], schema: 2);

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, kit.Sign(json), false));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("Upper")]
    [InlineData("kastom")]
    public void Verify_BadId_Throws(string id)
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry(id));

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_VersionWithPathChars_Throws()
    {
        using var kit = new DeliveryKit();
        var entry = DeliveryKit.PlainEntry();
        var bad = new Kastom.Core.Models.CatalogEntry
        {
            Id = entry.Id, Name = entry.Name, Version = "1.0.0-..\\..\\x", ContractVersion = 1, Url = entry.Url, Sha256 = entry.Sha256,
        };
        var (json, signature) = kit.SignedCatalog(bad);

        Assert.Throws<CatalogValidationException>(() => kit.Verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Verify_PlaceholderKey_ThrowsOnVerifyNotOnConstruction()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry());

        var verifier = new CatalogVerifier(KastomTrust.PlaceholderKey);

        var ex = Assert.Throws<CatalogValidationException>(() => verifier.Verify(json, signature, false));
        Assert.Contains("ключ", ex.Message);
    }

    [Fact]
    public void EmbeddedKey_IsARealP256Key_AndRejectsForeignSignature()
    {
        // Вшитий ключ — справжній: каталог, підписаний чужим ключем, відкидається через підпис, а не через «ключ не вшито».
        Assert.NotEqual(KastomTrust.PlaceholderKey, KastomTrust.CatalogPublicKey);
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry());

        var verifier = new CatalogVerifier(KastomTrust.CatalogPublicKey);

        var ex = Assert.Throws<CatalogValidationException>(() => verifier.Verify(json, signature, false));
        Assert.DoesNotContain("не вшито", ex.Message);
    }

    [Fact]
    public void Verify_GarbageKey_ThrowsOnVerify()
    {
        var verifier = new CatalogVerifier("AAAA");

        Assert.Throws<CatalogValidationException>(() => verifier.Verify([1], "AAAA", false));
    }

    [Fact]
    public void Verify_NonP256Key_Rejected()
    {
        using var kit = new DeliveryKit();
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var verifier = new CatalogVerifier(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()));
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry());

        Assert.Throws<CatalogValidationException>(() => verifier.Verify(json, signature, false));
    }

    [Fact]
    public void Compatibility_MatchingContractAndOldEnoughLauncher_IsCompatible()
    {
        Assert.True(ModuleCompatibility.Check(1, "0.1.0", ModuleVersion.Parse("0.1.0")).IsCompatible);
        Assert.True(ModuleCompatibility.Check(1, "1.0.0-beta", ModuleVersion.Parse("1.0.0")).IsCompatible);
    }

    [Fact]
    public void Compatibility_WrongContract_IsIncompatible()
    {
        var result = ModuleCompatibility.Check(2, "0.1.0", ModuleVersion.Parse("1.0.0"));

        Assert.False(result.IsCompatible);
        Assert.Contains("v2", result.Reason);
        Assert.Contains("v1", result.Reason);
    }

    [Fact]
    public void Compatibility_NewLauncherRequired_IsIncompatible()
    {
        var result = ModuleCompatibility.Check(1, "2.0.0", ModuleVersion.Parse("1.9.9"));

        Assert.False(result.IsCompatible);
        Assert.Contains("2.0.0", result.Reason);
    }

    [Fact]
    public void Compatibility_UnparsableMinimum_IsIncompatible() =>
        Assert.False(ModuleCompatibility.Check(1, "скоро", ModuleVersion.Parse("1.0.0")).IsCompatible);
}
