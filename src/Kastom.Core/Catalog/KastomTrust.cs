namespace Kastom.Core.Catalog;

/// <summary>
/// Відкритий ключ, яким перевіряється підпис каталогу (ECDSA P-256, SubjectPublicKeyInfo, base64).
/// Пара створена 2026-10-25 командою `kastom-catalog keygen`; секретна половина — у
/// %USERPROFILE%\.kastom\keys\catalog-signing.pem, поза репозиторієм і Google Диском.
/// </summary>
public static class KastomTrust
{
    /// <summary>Заглушка замість ключа; з нею лаунчер запускається, але жоден каталог не пройде перевірку.</summary>
    public const string PlaceholderKey = "REPLACE_WITH_REAL_KEY";

    public const string CatalogPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvNlb24B+fsHLkCblmS+wZAoIvpw0PD+YnDh9F6bEkg9Vc6v+fbm8bBNNUxbsfY6AyRT+4HLt2Nz5ByTyTNPV1w==";
}
