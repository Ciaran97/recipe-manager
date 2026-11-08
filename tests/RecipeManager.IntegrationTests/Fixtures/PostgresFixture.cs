using System.Security.Cryptography;
using Testcontainers.PostgreSql;

namespace RecipeManager.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18@sha256:3a4264520e121176619f54ff867d12774bc81e95e7b84db58d63bccdb6144b81";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("recipe_manager_tests")
        .WithUsername("recipe_tests")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
        .Build();

    internal PostgreSqlContainer Container => _container;
    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
