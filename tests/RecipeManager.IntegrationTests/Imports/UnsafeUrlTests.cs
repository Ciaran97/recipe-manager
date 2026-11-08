using System.Net;
using System.Text;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Imports;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class UnsafeUrlTests
{
    private sealed class Resolver(Func<string, IPAddress[]> resolve) : IImportDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(resolve(host)); }
    }
    private sealed class Transport(Func<Uri, HttpResponseMessage> respond) : IWebsiteHttpTransport
    {
        public List<Uri> Requests { get; } = [];
        public Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Requests.Add(uri); return Task.FromResult(respond(uri)); }
    }
    private sealed class SocketSpy : IPublicSocketConnector
    {
        public List<IPAddress> Addresses { get; } = [];
        public ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken ct) { Addresses.Add(address); return ValueTask.FromResult<Stream>(new MemoryStream()); }
    }
    private static IImportDnsResolver PublicDns() => new Resolver(_ => [IPAddress.Parse("8.8.8.8")]);
    private static HttpResponseMessage Page(string text = "<html>recipe</html>") => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/html") };

    [Theory]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("http://2130706433/private")]
    [InlineData("http://[::1]/private")]
    [InlineData("http://169.254.169.254/metadata")]
    [InlineData("http://user:password@example.org/")]
    [InlineData("file:///etc/passwd")]
    public async Task Unsafe_initial_urls_and_redirects_never_reach_the_unsafe_target(string target)
    {
        var transport = new Transport(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(target) } });
        var fetcher = new PublicWebsiteFetcher(PublicDns(), transport);
        var initial = await Assert.ThrowsAsync<ImportFailureException>(() => fetcher.FetchAsync(target, CancellationToken.None));
        Assert.Equal("import_unsafe_url", initial.Code); Assert.Empty(transport.Requests);
        var redirected = await Assert.ThrowsAsync<ImportFailureException>(() => fetcher.FetchAsync("https://example.org/recipe", CancellationToken.None));
        Assert.Equal("import_unsafe_url", redirected.Code); Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task Mixed_public_private_dns_and_rebinding_are_denied_before_socket_connection()
    {
        var mixed = new PublicWebsiteFetcher(new Resolver(_ => [IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]), new Transport(_ => Page()));
        await Assert.ThrowsAsync<ImportFailureException>(() => mixed.FetchAsync("https://recipe.example/", CancellationToken.None));
        var calls = 0; var resolver = new Resolver(_ => ++calls == 1 ? [IPAddress.Parse("8.8.8.8")] : [IPAddress.Loopback]);
        _ = await PublicAddressPolicy.ResolveAsync(resolver, "recipe.example", CancellationToken.None);
        var socket = new SocketSpy(); var connector = new PublicHttpConnector(resolver, socket);
        await Assert.ThrowsAsync<ImportFailureException>(async () => await connector.ConnectAsync(new("recipe.example", 443), CancellationToken.None));
        Assert.Empty(socket.Addresses);
    }

    [Fact]
    public async Task Connector_pins_a_validated_address_and_transport_disables_ambient_credentials_proxy_and_redirects()
    {
        var socket = new SocketSpy(); var connector = new PublicHttpConnector(PublicDns(), socket);
        using var stream = await connector.ConnectAsync(new("recipe.example", 443), CancellationToken.None);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), Assert.Single(socket.Addresses));
        using var handler = SafeWebsiteTransport.CreateHandler(connector);
        Assert.False(handler.UseProxy); Assert.False(handler.UseCookies); Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression); Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task Three_redirects_are_allowed_but_a_fourth_is_rejected()
    {
        var transport = new Transport(uri => uri.AbsolutePath == "/3" ? Page() : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/" + (int.Parse(uri.AbsolutePath.Trim('/')) + 1), UriKind.Relative) } });
        var fetcher = new PublicWebsiteFetcher(PublicDns(), transport);
        Assert.Equal("/3", (await fetcher.FetchAsync("https://example.org/0", CancellationToken.None)).FinalUri.AbsolutePath);
        Assert.Equal(4, transport.Requests.Count);
        var loop = new Transport(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/again", UriKind.Relative) } });
        var failure = await Assert.ThrowsAsync<ImportFailureException>(() => new PublicWebsiteFetcher(PublicDns(), loop).FetchAsync("https://example.org/", CancellationToken.None));
        Assert.Equal("import_redirect_limit", failure.Code); Assert.Equal(4, loop.Requests.Count);
    }

    [Fact]
    public async Task Oversized_decoded_content_invalid_content_types_and_failed_responses_are_rejected()
    {
        var tooLarge = new PublicWebsiteFetcher(PublicDns(), new Transport(_ => Page(new string('x', 2 * 1024 * 1024 + 1))));
        Assert.Equal("import_limit", (await Assert.ThrowsAsync<ImportFailureException>(() => tooLarge.FetchAsync("https://example.org/", CancellationToken.None))).Code);
        var image = new PublicWebsiteFetcher(PublicDns(), new Transport(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/png") } } }));
        Assert.Equal("import_unsupported_content", (await Assert.ThrowsAsync<ImportFailureException>(() => image.FetchAsync("https://example.org/", CancellationToken.None))).Code);
        var failed = new PublicWebsiteFetcher(PublicDns(), new Transport(_ => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private upstream details") }));
        Assert.Equal("import_fetch_failed", (await Assert.ThrowsAsync<ImportFailureException>(() => failed.FetchAsync("https://example.org/", CancellationToken.None))).Code);
    }
}
