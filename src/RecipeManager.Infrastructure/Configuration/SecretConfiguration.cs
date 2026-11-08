using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RecipeManager.Infrastructure.Configuration;

public static class SecretConfiguration
{
    public static string DatabaseConnection(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Configure ConnectionStrings:Database.");
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(configured); }
        catch (ArgumentException) { throw new InvalidOperationException("Database connection configuration is invalid."); }
        if (configuration["Database:PasswordFile"] is { Length: > 0 } passwordFile) connection.Password = ReadSecret(passwordFile);
        if (string.IsNullOrWhiteSpace(connection.Password)) throw new InvalidOperationException("Configure the database password using a secret file or protected development configuration.");
        connection.IncludeErrorDetail = false;
        connection.LogParameters = false;
        return connection.ConnectionString;
    }

    public static string ReadSecret(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is < 1 or > 65536) throw new InvalidOperationException("Secret file must exist and contain a bounded nonblank value.");
            var value = File.ReadAllText(path).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Secret file must contain a nonblank value.");
            return value;
        }
        catch (IOException) { throw new InvalidOperationException("Secret file could not be read."); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("Secret file could not be read."); }
    }
}
