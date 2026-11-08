using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using RecipeManager.Infrastructure.Configuration;

namespace RecipeManager.Api.Security;

public static class KeyProtection
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, bool ephemeral)
    {
        var protection = services.AddDataProtection().SetApplicationName("RecipeManager");
        if (ephemeral) { protection.UseEphemeralDataProtectionProvider(); return; }
        var directory = configuration["DataProtection:KeysDirectory"];
        var certificateFile = configuration["DataProtection:CertificateFile"];
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(certificateFile))
            throw new InvalidOperationException("Configure persistent Data Protection keys and an encryption certificate.");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var mode = File.GetUnixFileMode(directory);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException("Data Protection key directory must be private to its owner (0700).");
        }
        else Directory.CreateDirectory(directory);
        var password = configuration["DataProtection:CertificatePasswordFile"] is { Length: > 0 } path
            ? SecretConfiguration.ReadSecret(path) : null;
        X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12FromFile(certificateFile, password, OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet); }
        catch (System.Security.Cryptography.CryptographicException) { throw new InvalidOperationException("Data Protection certificate could not be loaded."); }
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("Data Protection certificate must contain its private key.");
        services.AddSingleton(certificate);
        protection.PersistKeysToFileSystem(new DirectoryInfo(directory)).ProtectKeysWithCertificate(certificate);
    }
}
