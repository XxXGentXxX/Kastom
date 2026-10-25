using System.Security.Cryptography;
using System.Text.Json;
using Kastom.Core.Models;

namespace Kastom.Core.Catalog;

/// <summary>Перевірка підпису й вмісту каталогу.</summary>
public sealed class CatalogVerifier
{
    /// <summary>Найбільший розмір catalog.json: 1 МБ.</summary>
    public const int MaxCatalogSize = 1024 * 1024;

    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    private readonly byte[]? _publicKey;
    private readonly string? _keyError;

    /// <summary>Не кидає винятків: заглушка чи пошкоджений ключ проявляються в <see cref="Verify"/>, щоб застосунок міг стартувати.</summary>
    public CatalogVerifier(string publicKeySpkiBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64) || publicKeySpkiBase64 == KastomTrust.PlaceholderKey)
        {
            _keyError = "Відкритий ключ каталогу ще не вшито в лаунчер (KastomTrust.CatalogPublicKey). Підпис перевірити неможливо.";
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(publicKeySpkiBase64.Trim());
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out _);
            if (!IsP256(key))
            {
                _keyError = "Відкритий ключ каталогу має бути ECDSA на кривій P-256.";
                return;
            }

            _publicKey = bytes;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            _keyError = "Відкритий ключ каталогу пошкоджений: це не ECDSA P-256 у форматі SubjectPublicKeyInfo (base64).";
        }
    }

    /// <summary>Повертає каталог або кидає <see cref="CatalogValidationException"/>.</summary>
    public ModuleCatalog Verify(byte[] catalogJson, string signatureBase64, bool allowFileUrls)
    {
        if (_publicKey is null) throw new CatalogValidationException(_keyError!);
        if (catalogJson.Length > MaxCatalogSize)
            throw new CatalogValidationException("Каталог завеликий: понад 1 МБ.");

        // Підпис IEEE P1363 для P-256 завжди рівно 64 байти.
        var signature = new byte[64];
        if (string.IsNullOrWhiteSpace(signatureBase64)
            || !Convert.TryFromBase64String(signatureBase64.Trim(), signature, out var written)
            || written != signature.Length)
            throw new CatalogValidationException("Підпис каталогу має неправильний формат.");

        bool valid;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(_publicKey, out _);
            valid = key.VerifyData(catalogJson, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            valid = false;
        }

        if (!valid) throw new CatalogValidationException("Підпис каталогу не збігається: каталог змінено або підписано іншим ключем.");

        return ParseAndValidate(catalogJson, allowFileUrls);
    }

    private static ModuleCatalog ParseAndValidate(byte[] catalogJson, bool allowFileUrls)
    {
        ModuleCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<ModuleCatalog>(catalogJson, KastomJson.Options);
        }
        catch (JsonException ex)
        {
            throw new CatalogValidationException($"Каталог не є правильним JSON: {ex.Message}");
        }

        if (catalog is null) throw new CatalogValidationException("Каталог порожній.");
        if (catalog.SchemaVersion != 1)
            throw new CatalogValidationException($"Непідтримувана версія схеми каталогу: {catalog.SchemaVersion}.");
        if (catalog.Modules is null) throw new CatalogValidationException("У каталозі немає списку модулів (modules).");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Modules)
        {
            if (entry is null) throw new CatalogValidationException("У списку модулів є порожній запис.");
            var problem = CatalogEntryValidator.Validate(entry, allowFileUrls);
            if (problem is not null) throw new CatalogValidationException(problem);
            if (!ids.Add(entry.Id))
                throw new CatalogValidationException($"Модуль '{entry.Id}' повторюється в каталозі.");
        }

        if (catalog.Launcher is not null)
        {
            var launcherProblem = CatalogEntryValidator.ValidateLauncher(catalog.Launcher);
            if (launcherProblem is not null) throw new CatalogValidationException(launcherProblem);
        }

        return catalog;
    }

    private static bool IsP256(ECDsa key)
    {
        if (key.KeySize != 256) return false;
        var curve = key.ExportParameters(false).Curve;
        if (!curve.IsNamed) return false;
        return curve.Oid.Value == NistP256Oid
            || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1";
    }
}
