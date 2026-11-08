using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Maintenance;

public sealed class MaintenanceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Private_socket_is_0600_requires_secret_and_cannot_be_reached_over_tcp()
    {
        if (OperatingSystem.IsWindows()) return;
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var user = new ApplicationUser(Guid.NewGuid(), "operator-fixture@example.test");
        var administrator = new ApplicationUser(Guid.NewGuid(), "admin-fixture@example.test");
        var household = new Household(Guid.NewGuid(), "Fixture", "UTC");
        db.AddRange(user, administrator, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Member),
            new Membership(Guid.NewGuid(), household.Id, administrator.Id, HouseholdRole.Administrator));
        await db.SaveChangesAsync();
        var originalStamp = user.SecurityStamp;
        var directory = Path.Combine(Path.GetTempPath(), "rm-op-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var socketPath = Path.Combine(directory, "op.sock");
        var secretFile = Path.Combine(directory, "secret");
        File.WriteAllText(secretFile, "private-test-only-maintenance-secret-long-enough");
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__Database"] = postgres.ConnectionString;
        start.Environment["Maintenance__SocketPath"] = socketPath;
        start.Environment["Maintenance__SecretFile"] = secretFile;
        start.Environment["Server__PublicAddress"] = $"http://127.0.0.1:{port}";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ConnectCallback = async (_, cancellation) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellation); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost"), Timeout = TimeSpan.FromSeconds(15) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("Maintenance test host exited before startup.");
                try { using var probe = await client.GetAsync("/__maintenance/ping", timeout.Token); break; }
                catch (HttpRequestException) { await Task.Delay(50, timeout.Token); }
            }
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(socketPath));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/__maintenance/ping")).StatusCode);
            client.DefaultRequestHeaders.Add("X-RecipeManager-Maintenance-Secret", "private-test-only-maintenance-secret-long-enough");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/__maintenance/ping")).StatusCode);
            using var publicClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            publicClient.DefaultRequestHeaders.Add("X-RecipeManager-Maintenance-Secret", "private-test-only-maintenance-secret-long-enough");
            Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/__maintenance/ping")).StatusCode);
            var rotation = await client.PostAsJsonAsync("/__maintenance/accounts/rotate-stamp", new { email = user.Email });
            Assert.Equal(HttpStatusCode.NoContent, rotation.StatusCode);
            Assert.NotEqual(originalStamp, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).SecurityStamp);
            publicClient.DefaultRequestHeaders.Add("X-Forwarded-For", "unix");
            using var malformed = new StringContent("{malformed", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.NotFound, (await publicClient.PostAsync("/__maintenance/accounts/reset-password", malformed)).StatusCode);
            var command = new ProcessStartInfo(start.FileName)
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var pair in start.Environment) command.Environment[pair.Key] = pair.Value;
            command.ArgumentList.Add(typeof(Program).Assembly.Location);
            command.ArgumentList.Add("maintenance"); command.ArgumentList.Add("reset-password");
            command.ArgumentList.Add(user.Email!); command.ArgumentList.Add("--password-stdin");
            using var cli = Process.Start(command)!;
            var output = cli.StandardOutput.ReadToEndAsync(); var errors = cli.StandardError.ReadToEndAsync();
            await cli.StandardInput.WriteLineAsync("replacement-test-passphrase"); cli.StandardInput.Close();
            await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, cli.ExitCode);
            Assert.DoesNotContain("replacement-test-passphrase", await output);
            Assert.DoesNotContain("replacement-test-passphrase", await errors);
            var changed = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
            Assert.NotEqual(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed,
                new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().VerifyHashedPassword(changed, changed.PasswordHash!, "replacement-test-passphrase"));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/__maintenance/accounts/deactivate", new { email = administrator.Email })).StatusCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _ = await stdout; _ = await stderr; // Never echo captured host configuration or credential requests.
            Directory.Delete(directory, true);
        }
    }
}
