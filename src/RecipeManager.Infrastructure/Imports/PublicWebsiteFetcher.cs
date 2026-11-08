using System.Net;
using System.Net.Sockets;
using System.Text;
using RecipeManager.Core.Modules.Imports;

namespace RecipeManager.Infrastructure.Imports;

public interface IPublicSocketConnector
{
    ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken);
}
public sealed class PublicSocketConnector : IPublicSocketConnector
{
    public async ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
public sealed class PublicHttpConnector(IImportDnsResolver resolver, IPublicSocketConnector sockets)
{
    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addresses = await PublicAddressPolicy.ResolveAsync(resolver, endpoint.Host, cancellationToken);
        foreach (var address in addresses)
        {
            try { return await sockets.ConnectAsync(address, endpoint.Port, cancellationToken); }
            catch (SocketException) { /* Another already-validated public address may work. */ }
        }
        throw new ImportFailureException("import_fetch_failed");
    }
}
public interface IWebsiteHttpTransport
{
    Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken);
}
public sealed class SafeWebsiteTransport : IWebsiteHttpTransport, IDisposable
{
    private readonly HttpClient _client;
    public SafeWebsiteTransport(PublicHttpConnector connector)
        => _client = new(CreateHandler(connector)) { Timeout = Timeout.InfiniteTimeSpan };
    public static SocketsHttpHandler CreateHandler(PublicHttpConnector connector) => new()
    {
        UseProxy = false,
        UseCookies = false,
        AllowAutoRedirect = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.All,
        MaxResponseHeadersLength = 32,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = (context, ct) => connector.ConnectAsync(context.DnsEndPoint, ct)
    };
    public async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.UserAgent.ParseAdd("RecipeManager/1.0");
        request.Headers.Accept.ParseAdd("text/html, application/xhtml+xml, application/ld+json, application/json");
        // The request host remains unchanged, so HttpClient supplies ordinary host/SNI/certificate
        // validation while ConnectCallback pins the TCP connection to its validated IP address.
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    public void Dispose() => _client.Dispose();
}
public sealed record ImportedWebPage(Uri FinalUri, string Content);
public sealed class PublicWebsiteFetcher(IImportDnsResolver resolver, IWebsiteHttpTransport transport)
{
    public const int MaximumDecodedBytes = 2 * 1024 * 1024;
    public async Task<ImportedWebPage> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var current = PublicAddressPolicy.Url(url);
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = await PublicAddressPolicy.ResolveAsync(resolver, current.IdnHost, cancellationToken);
                using var response = await transport.SendAsync(current, cancellationToken);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects >= 3) throw new ImportFailureException("import_redirect_limit");
                    if (response.Headers.Location is not { } next || !Uri.TryCreate(current, next, out var resolved)) throw new ImportFailureException("import_unsafe_url");
                    current = PublicAddressPolicy.Url(resolved.AbsoluteUri);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new ImportFailureException("import_fetch_failed");
                var type = response.Content.Headers.ContentType;
                if (type?.MediaType?.ToLowerInvariant() is not ("text/html" or "application/xhtml+xml" or "application/ld+json" or "application/json"))
                    throw new ImportFailureException("import_unsupported_content");
                if (response.Content.Headers.ContentLength > MaximumDecodedBytes) throw new ImportFailureException("import_limit");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var output = new MemoryStream(); var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (output.Length + count > MaximumDecodedBytes) throw new ImportFailureException("import_limit");
                    output.Write(buffer, 0, count);
                }
                var charset = type.CharSet?.Trim('"');
                var encoding = string.IsNullOrWhiteSpace(charset) ? new UTF8Encoding(false, true)
                    : Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                return new(current, encoding.GetString(output.GetBuffer(), 0, checked((int)output.Length)));
            }
        }
        catch (HttpRequestException error)
        {
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
                if (cause is ImportFailureException failure) throw failure;
            throw new ImportFailureException("import_fetch_failed");
        }
        catch (SocketException) { throw new ImportFailureException("import_fetch_failed"); }
        catch (IOException) { throw new ImportFailureException("import_fetch_failed"); }
        catch (DecoderFallbackException) { throw new ImportFailureException("import_invalid_document"); }
        catch (ArgumentException) { throw new ImportFailureException("import_invalid_document"); }
    }
}
