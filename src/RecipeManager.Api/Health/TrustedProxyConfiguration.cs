using System.Net;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;

namespace RecipeManager.Api.Health;

public static class TrustedProxyConfiguration
{
    public static void ConfigurePublicHost(this WebApplicationBuilder builder)
    {
        if (!Uri.TryCreate(builder.Configuration["Proxy:PublicOrigin"], UriKind.Absolute, out var origin)) return;
        builder.Services.PostConfigure<HostFilteringOptions>(options => options.AllowedHosts = [origin.IdnHost, "localhost", "127.0.0.1", "[::1]"]);
    }
    public static void UseTrustedProxy(this WebApplication app)
    {
        var configuration = app.Configuration;
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use explicit Proxy:TrustedAddresses instead of enabling unrestricted forwarded headers.");
        var addresses = configuration["Proxy:TrustedAddresses"];
        var originText = configuration["Proxy:PublicOrigin"];
        if (string.IsNullOrWhiteSpace(addresses) && string.IsNullOrWhiteSpace(originText)) return;
        if (string.IsNullOrWhiteSpace(addresses) || !Uri.TryCreate(originText, UriKind.Absolute, out var origin)
            || origin.Scheme != "https" || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Configure explicit trusted proxy IP addresses and an HTTPS public origin.");
        var peers = addresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (peers.Length is < 1 or > 16) throw new InvalidOperationException("Configure one to sixteen trusted proxy IP addresses.");
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
        foreach (var value in peers)
        {
            if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("Trusted proxy entries must be explicit IP addresses.");
            options.KnownProxies.Add(address);
        }
        options.AllowedHosts.Add(origin.IdnHost);
        app.UseForwardedHeaders(options);
    }
}
