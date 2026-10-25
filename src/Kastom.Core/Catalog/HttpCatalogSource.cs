using System.Net;
using System.Net.Http.Headers;
using Kastom.Core.Abstractions;

namespace Kastom.Core.Catalog;

/// <summary>Каталог із https-адреси: catalog.json і поруч catalog.json.sig.</summary>
public sealed class HttpCatalogSource(HttpClient http, Uri catalogUrl, CatalogVerifier verifier) : ICatalogSource
{
    private const int MaxSignatureSize = 4 * 1024;

    private readonly object _etagLock = new();
    private string? _etag;

    public bool AllowsFileUrls => false;

    /// <summary>Ліміт на все отримання (заголовки й тіло каталогу та підпису). Змінюється лише в тестах.</summary>
    internal TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public async Task<CatalogFetchResult> FetchAsync(CancellationToken callerToken)
    {
        if (!catalogUrl.IsAbsoluteUri || catalogUrl.Scheme != Uri.UriSchemeHttps)
            return Failed("Адреса каталогу має починатися з https://.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(FetchTimeout);
        var cancellationToken = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, catalogUrl);
            string? knownEtag;
            lock (_etagLock) knownEtag = _etag;
            if (knownEtag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", knownEtag);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new CatalogFetchResult { Status = CatalogFetchStatus.NotModified };
            if (!response.IsSuccessStatusCode)
                return Failed($"Сервер каталогу відповів кодом {(int)response.StatusCode}.");

            var catalogBytes = await ReadCappedAsync(response, CatalogVerifier.MaxCatalogSize, cancellationToken).ConfigureAwait(false);
            var etag = response.Headers.ETag?.ToString();

            var signatureUrl = new UriBuilder(catalogUrl) { Path = catalogUrl.AbsolutePath + ".sig" }.Uri;
            using var signatureResponse = await http.GetAsync(signatureUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!signatureResponse.IsSuccessStatusCode)
                return Failed($"Підпис каталогу не отримано: код {(int)signatureResponse.StatusCode}.");
            var signatureBytes = await ReadCappedAsync(signatureResponse, MaxSignatureSize, cancellationToken).ConfigureAwait(false);
            var signature = System.Text.Encoding.UTF8.GetString(signatureBytes).Trim();

            var catalog = verifier.Verify(catalogBytes, signature, allowFileUrls: false);
            return new CatalogFetchResult
            {
                Status = CatalogFetchStatus.Ok,
                Catalog = catalog,
                RawCatalog = catalogBytes,
                Signature = signature,
                ETag = etag,
            };
        }
        catch (CatalogValidationException ex)
        {
            return new CatalogFetchResult { Status = CatalogFetchStatus.InvalidSignature, Error = ex.Message };
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed("Сервер каталогу не відповів вчасно.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UriFormatException)
        {
            return Failed($"Каталог не отримано: {ex.Message}");
        }
    }

    /// <summary>ETag запам'ятовується лише для каталогу, який прийняв CatalogService: відкинутий не повинен давати 304 надалі.</summary>
    public void MarkAccepted(CatalogFetchResult result)
    {
        lock (_etagLock) _etag = result.ETag;
    }

    private static CatalogFetchResult Failed(string error) =>
        new() { Status = CatalogFetchStatus.Failed, Error = error };

    /// <summary>Читає тіло потоком із жорстким обмеженням: заголовку Content-Length не вірить.</summary>
    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, int cap, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > cap)
            throw new InvalidDataException($"Відповідь сервера завелика (ліміт {cap} байтів).");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > cap)
                throw new InvalidDataException($"Відповідь сервера завелика (ліміт {cap} байтів).");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
