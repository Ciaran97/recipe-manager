using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RecipeManager.IntegrationTests.Fixtures;

public sealed class ApiFixture(string connectionString, IReadOnlyDictionary<string, string?>? settings = null, TimeProvider? clock = null, Action<IServiceCollection>? configure = null, string? webRoot = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (webRoot is not null) builder.UseWebRoot(webRoot);
        if (configure is not null) builder.ConfigureServices(configure);
        if (clock is not null) builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(clock)));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["StarterData:Enabled"] = "false", ["ConnectionStrings:Database"] = connectionString }
                .Concat(settings ?? new Dictionary<string, string?>()).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())));
    }
}
