using Microsoft.Extensions.Configuration;
using Npgsql;
using RecipeManager.Infrastructure.Configuration;

namespace RecipeManager.IntegrationTests.Configuration;

public sealed class SecretConfigurationTests
{
    [Fact]
    public void Password_file_is_read_without_becoming_part_of_errors_or_parameter_logging()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "fixture-long-random-password\n");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=recipe;Username=recipe;Include Error Detail=true;Log Parameters=true",
                ["Database:PasswordFile"] = path
            }).Build();
            var connection = new NpgsqlConnectionStringBuilder(SecretConfiguration.DatabaseConnection(config));
            Assert.Equal("fixture-long-random-password", connection.Password);
            Assert.False(connection.IncludeErrorDetail);
            Assert.False(connection.LogParameters);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void Missing_database_and_blank_secret_fail_without_disclosing_values()
    {
        Assert.Throws<InvalidOperationException>(() => SecretConfiguration.DatabaseConnection(new ConfigurationBuilder().Build()));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "   \n");
            Assert.Throws<InvalidOperationException>(() => SecretConfiguration.ReadSecret(path));
        }
        finally { File.Delete(path); }
    }
}
