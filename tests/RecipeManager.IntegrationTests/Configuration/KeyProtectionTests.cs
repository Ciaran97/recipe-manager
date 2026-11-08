using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Security;

namespace RecipeManager.IntegrationTests.Configuration;

public sealed class KeyProtectionTests
{
    [Fact]
    public void Persistent_keys_are_encrypted_and_work_after_provider_recreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "recipe-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=RecipeManager test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificateFile = Path.Combine(directory, "certificate.pfx");
            var passwordFile = Path.Combine(directory, "certificate-password");
            File.WriteAllBytes(certificateFile, certificate.Export(X509ContentType.Pkcs12, "test-only-password"));
            File.WriteAllText(passwordFile, "test-only-password");
            var keys = Path.Combine(directory, "keys");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysDirectory"] = keys,
                ["DataProtection:CertificateFile"] = certificateFile,
                ["DataProtection:CertificatePasswordFile"] = passwordFile
            }).Build();
            ServiceProvider Provider()
            {
                var services = new ServiceCollection().AddLogging();
                KeyProtection.Configure(services, config, false);
                return services.BuildServiceProvider();
            }
            string protectedValue;
            using (var first = Provider())
                protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("private-fixture-value");
            var keyXml = File.ReadAllText(Assert.Single(Directory.GetFiles(keys, "*.xml")));
            Assert.Contains("encryptedSecret", keyXml);
            Assert.DoesNotContain("private-fixture-value", keyXml);
            using var second = Provider();
            Assert.Equal("private-fixture-value", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(protectedValue));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Missing_persistent_key_configuration_is_rejected()
        => Assert.Throws<InvalidOperationException>(() => KeyProtection.Configure(new ServiceCollection(), new ConfigurationBuilder().Build(), false));
}
