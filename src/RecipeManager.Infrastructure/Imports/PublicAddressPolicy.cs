using System.Net;
using System.Net.Sockets;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.Imports;

public interface IImportDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}
public sealed class ImportDnsResolver : IImportDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(host, cancellationToken);
}
public static class PublicAddressPolicy
{
    private static readonly IPNetwork[] DeniedV4 = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24",
        "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4", "168.63.129.16/32"
    }.Select(IPNetwork.Parse).ToArray();
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] DeniedV6 = new[] { "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20" }.Select(IPNetwork.Parse).ToArray();
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !DeniedV4.Any(x => x.Contains(address)),
            AddressFamily.InterNetworkV6 => address.ScopeId == 0 && GlobalV6.Contains(address) && !DeniedV6.Any(x => x.Contains(address)),
            _ => false
        };
    }
    public static Uri Url(string value)
    {
        try { _ = DomainRules.HttpUrl(value, "url"); }
        catch (DomainValidationException) { throw new ImportFailureException("import_unsafe_url"); }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || string.IsNullOrWhiteSpace(uri.IdnHost))
            throw new ImportFailureException("import_unsafe_url");
        return uri;
    }
    public static async Task<IPAddress[]> ResolveAsync(IImportDnsResolver resolver, string host, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await resolver.ResolveAsync(host, ct);
        if (addresses.Length == 0 || addresses.Length > 64 || addresses.Any(x => !IsPublic(x))) throw new ImportFailureException("import_unsafe_url");
        return addresses.Distinct().ToArray();
    }
}
