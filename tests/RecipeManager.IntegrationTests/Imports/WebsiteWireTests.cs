using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Imports;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class WebsiteWireTests
{
    private sealed class Resolver : IImportDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
    }
    // Test-only connection routing. Production receives no loopback exception or test-host allowlist.
    private sealed class FixtureSocket(int port) : IPublicSocketConnector
    {
        public async ValueTask<Stream> ConnectAsync(IPAddress address, int ignoredPort, CancellationToken ct)
        {
            Assert.True(PublicAddressPolicy.IsPublic(address));
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(IPAddress.Loopback, port, ct); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }
    }
    private static async Task<string> ReadHeaders(Stream stream, CancellationToken ct)
    {
        using var data = new MemoryStream(); var single = new byte[1];
        while (data.Length < 32768)
        {
            if (await stream.ReadAsync(single, ct) == 0) throw new IOException("Fixture connection ended early.");
            data.WriteByte(single[0]);
            var text = Encoding.ASCII.GetString(data.GetBuffer(), 0, checked((int)data.Length));
            if (text.EndsWith("\r\n\r\n", StringComparison.Ordinal)) return text;
        }
        throw new IOException("Fixture request headers too large.");
    }
    private static async Task Write(Stream stream, string headers, byte[] body, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct); await stream.FlushAsync(ct);
    }

    [Fact]
    public async Task Real_transport_preserves_host_follows_only_validated_redirects_and_never_sends_cookies_or_fetches_images()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var index = 0; index < 2; index++)
            {
                using var socket = await listener.AcceptTcpClientAsync(ct); await using var stream = socket.GetStream();
                requests.Add(await ReadHeaders(stream, ct));
                if (index == 0) await Write(stream, "HTTP/1.1 302 Found\r\nLocation: /recipe\r\nSet-Cookie: private-site-session=secret\r\nContent-Length: 0\r\nConnection: close", [], ct);
                else
                {
                    var body = Encoding.UTF8.GetBytes("""<script type="application/ld+json">{"@type":"Recipe","name":"Wire fixture","image":"http://127.0.0.1/private-image","recipeIngredient":["salt to taste"]}</script><img src="/trap">""");
                    await Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close", body, ct);
                }
            }
        }, ct);
        var dns = new Resolver(); using var transport = new SafeWebsiteTransport(new(dns, new FixtureSocket(port)));
        var result = await new WebsiteRecipeImporter(new(dns, transport)).ImportAsync("http://recipe.example.test/", ct);
        await server;
        Assert.Single(result.Recipes); Assert.Equal(2, requests.Count);
        Assert.StartsWith("GET / HTTP/1.1", requests[0]); Assert.StartsWith("GET /recipe HTTP/1.1", requests[1]);
        Assert.All(requests, request =>
        {
            Assert.Contains("Host: recipe.example.test", request);
            Assert.DoesNotContain("Cookie:", request, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Referer:", request, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_gzip_and_chunked_responses_cannot_bypass_decoded_body_limit(bool gzip)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var expanded = Encoding.UTF8.GetBytes(new string('x', PublicWebsiteFetcher.MaximumDecodedBytes + 1));
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(ct); await using var stream = socket.GetStream();
            _ = await ReadHeaders(stream, ct);
            if (gzip)
            {
                using var compressed = new MemoryStream();
                using (var encoder = new GZipStream(compressed, CompressionLevel.SmallestSize, true)) encoder.Write(expanded);
                var body = compressed.ToArray();
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Encoding: gzip\r\nContent-Length: " + body.Length + "\r\nConnection: close", body, ct);
            }
            else
            {
                var prefix = Encoding.ASCII.GetBytes(expanded.Length.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
                var suffix = Encoding.ASCII.GetBytes("\r\n0\r\n\r\n");
                var body = prefix.Concat(expanded).Concat(suffix).ToArray();
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nTransfer-Encoding: chunked\r\nConnection: close", body, ct);
            }
        }, ct);
        var dns = new Resolver(); using var transport = new SafeWebsiteTransport(new(dns, new FixtureSocket(port)));
        var error = await Assert.ThrowsAsync<ImportFailureException>(() => new PublicWebsiteFetcher(dns, transport).FetchAsync("http://recipe.example.test/", ct));
        Assert.Equal("import_limit", error.Code); await server;
    }
}
