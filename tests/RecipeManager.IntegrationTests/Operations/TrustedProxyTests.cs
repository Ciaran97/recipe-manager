using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Operations;

public sealed class TrustedProxyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("192.0.2.10", "recipes.example.test", HttpStatusCode.Unauthorized)]
    [InlineData("192.0.2.11", "recipes.example.test", HttpStatusCode.Forbidden)]
    [InlineData("192.0.2.10", "other.example.test", HttpStatusCode.Forbidden)]
    public async Task Only_the_configured_proxy_and_public_host_can_reconstruct_the_hub_origin(string peer, string forwardedHost, HttpStatusCode expected)
    {
        await using var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Proxy:TrustedAddresses"] = "192.0.2.10",
            ["Proxy:PublicOrigin"] = "https://recipes.example.test"
        }, configure: services => services.AddSingleton<IStartupFilter>(new PeerAddress(IPAddress.Parse(peer))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/shopping/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "https://recipes.example.test"); request.Headers.Add("X-Forwarded-For", "198.51.100.5");
        request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-Host", forwardedHost);
        Assert.Equal(expected, (await client.SendAsync(request)).StatusCode);
    }
    private sealed class PeerAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = address; return continuation(); }); next(app);
        };
    }
}
