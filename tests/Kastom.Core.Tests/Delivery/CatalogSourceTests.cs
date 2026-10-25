using System.Net.Http;
using System.IO;
using System.Net;
using Kastom.Core.Abstractions;
using Kastom.Core.Catalog;
using Kastom.Core.Models;

namespace Kastom.Core.Tests.Delivery;

public class CatalogSourceTests
{
    private static readonly Uri CatalogUrl = new("https://example.com/catalog/catalog.json");
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FileSource_RoundTrip_ReturnsVerifiedCatalog()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(DeliveryKit.PlainEntry("alpha", "file:///C:/pkg/alpha.zip"));

        var result = await new FileCatalogSource(dir, kit.Verifier).FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Ok, result.Status);
        Assert.Equal("alpha", result.Catalog!.Modules.Single().Id);
        Assert.NotNull(result.RawCatalog);
        Assert.NotEmpty(result.Signature!);
    }

    [Fact]
    public async Task FileSource_TamperedCatalog_IsInvalidSignature()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(DeliveryKit.PlainEntry("alpha"));
        var path = Path.Combine(dir, ModuleCatalog.FileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("alpha", "alphb"));

        var result = await new FileCatalogSource(dir, kit.Verifier).FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public async Task FileSource_MissingFiles_IsFailed()
    {
        using var kit = new DeliveryKit();

        var result = await new FileCatalogSource(Path.Combine(kit.Root, "nope"), kit.Verifier).FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task HttpSource_HttpCatalogUrl_IsFailed()
    {
        using var kit = new DeliveryKit();
        var source = new HttpCatalogSource(new HttpClient(new FailingHandler()), new Uri("http://example.com/catalog.json"), kit.Verifier);

        Assert.Equal(CatalogFetchStatus.Failed, (await source.FetchAsync(CancellationToken.None)).Status);
        Assert.False(source.AllowsFileUrls);
    }

    [Fact]
    public async Task HttpSource_Ok_ThenNotModifiedWithEtag()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry("alpha"));
        var handler = new FuncHandler(request =>
        {
            if (request.Headers.TryGetValues("If-None-Match", out var etags) && etags.Single() == "\"v1\"")
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            var response = new HttpResponseMessage
            {
                Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                    ? System.Text.Encoding.UTF8.GetBytes(signature + "\n")
                    : json),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return response;
        });
        var source = new HttpCatalogSource(new HttpClient(handler), CatalogUrl, kit.Verifier);

        var first = await source.FetchAsync(CancellationToken.None);
        source.MarkAccepted(first); // ETag запам'ятовується лише для прийнятого каталогу
        var second = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Ok, first.Status);
        Assert.Equal("\"v1\"", first.ETag);
        Assert.Equal("alpha", first.Catalog!.Modules.Single().Id);
        Assert.Equal(CatalogFetchStatus.NotModified, second.Status);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/catalog/catalog.json.sig");
    }

    [Fact]
    public async Task HttpSource_WithoutMarkAccepted_DoesNotSendEtag()
    {
        using var kit = new DeliveryKit();
        var server = new EtagServer(kit, Catalog("alpha", T0), "\"v1\"");
        var source = new HttpCatalogSource(new HttpClient(new FuncHandler(server.Respond)), CatalogUrl, kit.Verifier);

        await source.FetchAsync(CancellationToken.None);
        var second = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Ok, second.Status);
        Assert.Equal<string?>([null, null], server.IfNoneMatch);
    }

    [Fact]
    public async Task Service_AcceptedCatalog_NextRequestSendsItsEtag()
    {
        using var kit = new DeliveryKit();
        var server = new EtagServer(kit, Catalog("alpha", T0), "\"v1\"");
        var service = new CatalogService(
            new HttpCatalogSource(new HttpClient(new FuncHandler(server.Respond)), CatalogUrl, kit.Verifier), kit.Verifier, kit.Paths, kit.Log);

        var first = await service.RefreshAsync(CancellationToken.None);
        var second = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Ok, first.Status);
        Assert.Equal(CatalogFetchStatus.NotModified, second.Status);
        Assert.Equal<string?>([null, "\"v1\""], server.IfNoneMatch);
    }

    [Fact]
    public async Task Service_RollbackCatalog_DoesNotReplaceEtagOfAcceptedOne()
    {
        using var kit = new DeliveryKit();
        var server = new EtagServer(kit, Catalog("alpha", T0), "\"v1\"");
        var service = new CatalogService(
            new HttpCatalogSource(new HttpClient(new FuncHandler(server.Respond)), CatalogUrl, kit.Verifier), kit.Verifier, kit.Paths, kit.Log);
        await service.RefreshAsync(CancellationToken.None);

        server.Set(Catalog("old", T0.AddMinutes(-1)), "\"v2\"");
        var rollback = await service.RefreshAsync(CancellationToken.None);
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, rollback.Status);
        Assert.Equal("alpha", service.Current!.Modules.Single().Id);
        // Другий і третій запити шлють ETag прийнятого каталогу, а не відкинутого.
        Assert.Equal<string?>([null, "\"v1\"", "\"v1\""], server.IfNoneMatch);
    }

    [Fact]
    public async Task Service_RollbackCatalogAfterRestart_NextRequestSendsNoEtag()
    {
        using var kit = new DeliveryKit();
        var first = new EtagServer(kit, Catalog("alpha", T0), "\"v1\"");
        await new CatalogService(
                new HttpCatalogSource(new HttpClient(new FuncHandler(first.Respond)), CatalogUrl, kit.Verifier), kit.Verifier, kit.Paths, kit.Log)
            .RefreshAsync(CancellationToken.None);

        // Після перезапуску джерело нічого не пам'ятає, а прийнятий каталог береться з кешу.
        var server = new EtagServer(kit, Catalog("old", T0.AddMinutes(-1)), "\"v2\"");
        var restarted = new CatalogService(
            new HttpCatalogSource(new HttpClient(new FuncHandler(server.Respond)), CatalogUrl, kit.Verifier), kit.Verifier, kit.Paths, kit.Log);
        var rollback = await restarted.RefreshAsync(CancellationToken.None);
        await restarted.RefreshAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, rollback.Status);
        Assert.Equal("alpha", restarted.Current!.Modules.Single().Id);
        Assert.Equal<string?>([null, null], server.IfNoneMatch);
    }

    [Fact]
    public async Task HttpSource_BadSignature_IsInvalidSignature()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry("alpha"));
        json[10] ^= 1;
        var handler = new FuncHandler(request => new HttpResponseMessage
        {
            Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? System.Text.Encoding.UTF8.GetBytes(signature)
                : json),
        });

        var result = await new HttpCatalogSource(new HttpClient(handler), CatalogUrl, kit.Verifier).FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public async Task HttpSource_ServerErrorAndNetworkError_AreFailed()
    {
        using var kit = new DeliveryKit();
        var serverError = new HttpCatalogSource(
            new HttpClient(new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))), CatalogUrl, kit.Verifier);
        var networkError = new HttpCatalogSource(
            new HttpClient(new FuncHandler(_ => throw new HttpRequestException("немає мережі"))), CatalogUrl, kit.Verifier);

        Assert.Equal(CatalogFetchStatus.Failed, (await serverError.FetchAsync(CancellationToken.None)).Status);
        Assert.Equal(CatalogFetchStatus.Failed, (await networkError.FetchAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task HttpSource_BodyOverCapWithoutContentLength_IsFailed()
    {
        using var kit = new DeliveryKit();
        var handler = new FuncHandler(_ => new HttpResponseMessage { Content = new EndlessContent() });

        var result = await new HttpCatalogSource(new HttpClient(handler), CatalogUrl, kit.Verifier).FetchAsync(CancellationToken.None);

        Assert.Equal(CatalogFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task HttpSource_Cancelled_Throws()
    {
        using var kit = new DeliveryKit();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var source = new HttpCatalogSource(new HttpClient(new FuncHandler(_ => new HttpResponseMessage())), CatalogUrl, kit.Verifier);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchAsync(cts.Token));
    }

    [Fact]
    public async Task Service_Refresh_SetsCurrent_WritesCache_RaisesEventOnlyOnChange()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(DeliveryKit.PlainEntry("alpha", "file:///C:/pkg/alpha.zip"));
        var service = new CatalogService(new FileCatalogSource(dir, kit.Verifier), kit.Verifier, kit.Paths, kit.Log);
        var raised = 0;
        service.CatalogChanged += (_, _) => raised++;

        Assert.Null(service.Current);
        await service.RefreshAsync(CancellationToken.None);
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal("alpha", service.Current!.Modules.Single().Id);
        Assert.NotNull(service.LastChecked);
        Assert.Equal(1, raised);
        Assert.True(service.AllowsFileUrls);
        Assert.True(File.Exists(Path.Combine(kit.Paths.CatalogCacheRoot + "-dev", ModuleCatalog.FileName)));
        Assert.True(File.Exists(Path.Combine(kit.Paths.CatalogCacheRoot + "-dev", ModuleCatalog.SignatureFileName)));
        // Каталог розробника не потрапляє в кеш справжнього каталогу.
        Assert.False(File.Exists(Path.Combine(kit.Paths.CatalogCacheRoot, ModuleCatalog.FileName)));

        kit.WriteCatalog(DeliveryKit.PlainEntry("alpha", "file:///C:/pkg/alpha.zip"), DeliveryKit.PlainEntry("beta", "file:///C:/pkg/beta.zip"));
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, service.Current.Modules.Count);
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Service_CacheReloadsAfterRestart_AndFailedFetchKeepsIt()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(DeliveryKit.PlainEntry("alpha"));
        await new CatalogService(new FileCatalogSource(dir, kit.Verifier), kit.Verifier, kit.Paths, kit.Log)
            .RefreshAsync(CancellationToken.None);

        var restarted = new CatalogService(new FakeSource(CatalogFetchStatus.Failed, allowsFileUrls: true), kit.Verifier, kit.Paths, kit.Log);
        Assert.Equal("alpha", restarted.Current!.Modules.Single().Id);

        await restarted.RefreshAsync(CancellationToken.None);

        Assert.Equal("alpha", restarted.Current!.Modules.Single().Id);
        Assert.Contains(kit.Log.Messages, m => m.StartsWith("warn:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Service_TamperedCache_IsIgnored()
    {
        using var kit = new DeliveryKit();
        var dir = kit.WriteCatalog(DeliveryKit.PlainEntry("alpha"));
        await new CatalogService(new FileCatalogSource(dir, kit.Verifier), kit.Verifier, kit.Paths, kit.Log)
            .RefreshAsync(CancellationToken.None);
        var cached = Path.Combine(kit.Paths.CatalogCacheRoot + "-dev", ModuleCatalog.FileName);
        File.WriteAllText(cached, File.ReadAllText(cached).Replace("alpha", "alphb"));

        var restarted = new CatalogService(new FakeSource(CatalogFetchStatus.Failed, allowsFileUrls: true), kit.Verifier, kit.Paths, kit.Log);

        Assert.Null(restarted.Current);
    }

    [Fact]
    public void Service_CacheWithFileUrls_IgnoredWhenSourceDoesNotAllowThem()
    {
        using var kit = new DeliveryKit();
        var (json, signature) = kit.SignedCatalog(DeliveryKit.PlainEntry("alpha", "file:///C:/pkg/alpha.zip"));
        Directory.CreateDirectory(kit.Paths.CatalogCacheRoot);
        File.WriteAllBytes(Path.Combine(kit.Paths.CatalogCacheRoot, ModuleCatalog.FileName), json);
        File.WriteAllText(Path.Combine(kit.Paths.CatalogCacheRoot, ModuleCatalog.SignatureFileName), signature);

        var service = new CatalogService(new FakeSource(CatalogFetchStatus.Failed), kit.Verifier, kit.Paths, kit.Log);

        Assert.Null(service.Current);
    }

    private static byte[] Catalog(string id, DateTimeOffset at) => DeliveryKit.CatalogBytes([DeliveryKit.PlainEntry(id)], generatedAt: at);

    /// <summary>Сервер каталогу з ETag: відповідає 304 на збіг If-None-Match і записує, що прийшло в запитах на catalog.json.</summary>
    private sealed class EtagServer(DeliveryKit kit, byte[] json, string etag)
    {
        private byte[] _json = json;
        private string _etag = etag;

        public List<string?> IfNoneMatch { get; } = [];

        public void Set(byte[] newJson, string newEtag)
        {
            _json = newJson;
            _etag = newEtag;
        }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal))
                return new HttpResponseMessage { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(kit.Sign(_json))) };

            var sent = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            IfNoneMatch.Add(sent);
            if (sent == _etag) return new HttpResponseMessage(HttpStatusCode.NotModified);

            var response = new HttpResponseMessage { Content = new ByteArrayContent(_json) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(_etag);
            return response;
        }
    }

    private sealed class FakeSource(CatalogFetchStatus status, bool allowsFileUrls = false) : ICatalogSource
    {
        public bool AllowsFileUrls => allowsFileUrls;

        public Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogFetchResult { Status = status, Error = "тест" });
    }

    /// <summary>Тіло без відомої довжини, що не закінчується: перевіряє жорстке обмеження читання.</summary>
    private sealed class EndlessContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new EndlessStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'x', offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
