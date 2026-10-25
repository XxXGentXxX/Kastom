using Kastom.Core.Abstractions;
using Kastom.Core.Models;

namespace Kastom.Core.Catalog;

/// <summary>Локальний каталог розробника: catalog.json і catalog.json.sig у папці. Дозволяє file://-адреси пакетів.</summary>
public sealed class FileCatalogSource(string directory, CatalogVerifier verifier) : ICatalogSource
{
    private const int MaxSignatureSize = 4 * 1024;

    public bool AllowsFileUrls => true;

    public async Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalogPath = Path.Combine(directory, ModuleCatalog.FileName);
            var signaturePath = Path.Combine(directory, ModuleCatalog.SignatureFileName);
            if (!File.Exists(catalogPath) || !File.Exists(signaturePath))
                return Failed($"У папці '{directory}' немає {ModuleCatalog.FileName} або {ModuleCatalog.SignatureFileName}.");
            if (new FileInfo(catalogPath).Length > CatalogVerifier.MaxCatalogSize || new FileInfo(signaturePath).Length > MaxSignatureSize)
                return new CatalogFetchResult { Status = CatalogFetchStatus.InvalidSignature, Error = "Каталог або підпис завеликі." };

            var catalogBytes = await File.ReadAllBytesAsync(catalogPath, cancellationToken).ConfigureAwait(false);
            var signature = (await File.ReadAllTextAsync(signaturePath, cancellationToken).ConfigureAwait(false)).Trim();

            var catalog = verifier.Verify(catalogBytes, signature, allowFileUrls: true);
            return new CatalogFetchResult
            {
                Status = CatalogFetchStatus.Ok,
                Catalog = catalog,
                RawCatalog = catalogBytes,
                Signature = signature,
            };
        }
        catch (CatalogValidationException ex)
        {
            return new CatalogFetchResult { Status = CatalogFetchStatus.InvalidSignature, Error = ex.Message };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failed($"Каталог не прочитано: {ex.Message}");
        }
    }

    private static CatalogFetchResult Failed(string error) =>
        new() { Status = CatalogFetchStatus.Failed, Error = error };
}
