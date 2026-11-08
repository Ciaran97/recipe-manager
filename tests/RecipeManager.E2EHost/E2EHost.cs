using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace RecipeManager.Testing;

internal static class E2EHost
{
    public static async Task<int> Main(string[] args)
    {
        var directory = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "tests/e2e/.runtime");
        var port = int.Parse(args.ElementAtOrDefault(1) ?? "7285", System.Globalization.CultureInfo.InvariantCulture);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
        using var termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });
        var setupFile = Path.Combine(directory, "setup-token");
        var certFile = Path.Combine(directory, "localhost.pfx");
        var publicFile = Path.Combine(directory, "localhost.pem");
        var privateFile = Path.Combine(directory, "localhost.key");
        var setupToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var certificatePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(setupFile, setupToken);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost"); names.AddIpAddress(System.Net.IPAddress.Loopback); names.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(certFile, certificate.Export(X509ContentType.Pkcs12, certificatePassword));
        File.WriteAllText(publicFile, certificate.ExportCertificatePem());
        File.WriteAllText(privateFile, rsa.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows())
            foreach (var file in new[] { setupFile, certFile, publicFile, privateFile }) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await using var postgres = new PostgreSqlBuilder("postgres:18@sha256:3a4264520e121176619f54ff867d12774bc81e95e7b84db58d63bccdb6144b81")
            .WithDatabase("recipe_e2e").WithUsername("recipe_e2e").WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).Build();
        Process? api = null;
        Microsoft.AspNetCore.Builder.WebApplication? importProvider = null;
        try
        {
            await postgres.StartAsync(stop.Token);
            await using (var database = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.GetConnectionString()).Options))
                await database.Database.MigrateAsync(stop.Token);
            importProvider = await ImportProviderFixture.StartAsync(directory, stop.Token);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(global::Program).Assembly.Location);
            start.Environment["DOTNET_ENVIRONMENT"] = "Testing"; start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            start.Environment["ConnectionStrings__Database"] = postgres.GetConnectionString();
            start.Environment.Remove("Database__PasswordFile"); start.Environment.Remove("Maintenance__SocketPath");
            start.Environment["ASPNETCORE_URLS"] = $"https://localhost:{port}";
            start.Environment["Kestrel__Certificates__Default__Path"] = certFile;
            start.Environment["Kestrel__Certificates__Default__Password"] = certificatePassword;
            start.Environment["Setup__TokenFile"] = setupFile;
            start.Environment["StarterData__Enabled"] = "false";
            start.Environment["Images__Directory"] = Path.Combine(directory, "images");
            api = Process.Start(start) ?? throw new InvalidOperationException();
            var output = CopySafeOutput(api.StandardOutput);
            var errors = CopySafeOutput(api.StandardError);
            Console.WriteLine($"Isolated E2E fixture starting on https://localhost:{port}. No development database is used.");
            var exited = api.WaitForExitAsync();
            await Task.WhenAny(exited, Task.Delay(Timeout.Infinite, stop.Token));
            if (!stop.IsCancellationRequested) throw new InvalidOperationException("E2E child host exited.");
            if (!api.HasExited) api.Kill(entireProcessTree: true);
            await api.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) { Console.Error.WriteLine("E2E fixture failed: " + error.GetType().Name); return 1; }
        finally
        {
            if (api is { HasExited: false }) { api.Kill(entireProcessTree: true); await api.WaitForExitAsync(); }
            api?.Dispose();
            if (importProvider is not null) { await importProvider.StopAsync(); await importProvider.DisposeAsync(); }
            File.Delete(Path.Combine(directory, "ai-origin")); File.Delete(Path.Combine(directory, "ai-recipe-text"));
            foreach (var file in new[] { setupFile, certFile, publicFile, privateFile }) File.Delete(file);
            if (Directory.Exists(Path.Combine(directory, "images"))) Directory.Delete(Path.Combine(directory, "images"), recursive: true);
        }
    }
    private static async Task CopySafeOutput(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) Console.WriteLine(line);
    }
}
