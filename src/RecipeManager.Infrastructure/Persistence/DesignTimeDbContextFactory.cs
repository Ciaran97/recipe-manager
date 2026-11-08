using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using RecipeManager.Infrastructure.Configuration;

namespace RecipeManager.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RecipeManagerDbContext>
{
    public RecipeManagerDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Database externally; no database credentials are stored in source.");
        if (Environment.GetEnvironmentVariable("Database__PasswordFile") is { } passwordFile)
            connection = new NpgsqlConnectionStringBuilder(connection)
            {
                Password = SecretConfiguration.ReadSecret(passwordFile),
                IncludeErrorDetail = false,
                LogParameters = false
            }.ConnectionString;
        var options = new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(connection).Options;
        return new RecipeManagerDbContext(options);
    }
}
