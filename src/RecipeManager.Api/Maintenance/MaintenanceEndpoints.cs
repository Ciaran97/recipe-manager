using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Configuration;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Maintenance;

public sealed class MaintenanceAccess
{
    private readonly byte[]? _secretHash;
    public string? SocketPath { get; }
    public bool Enabled => SocketPath is not null;
    public MaintenanceAccess() { }
    public MaintenanceAccess(string socketPath, string secret)
    {
        SocketPath = socketPath;
        _secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
    public bool IsPrivateConnection(HttpContext context)
        => Enabled && context.Features.Get<IConnectionSocketFeature>()?.Socket.LocalEndPoint is UnixDomainSocketEndPoint endpoint
            && string.Equals(endpoint.ToString(), SocketPath, StringComparison.Ordinal);
    public bool Authorized(HttpContext context)
    {
        if (!IsPrivateConnection(context) || !context.Request.Headers.TryGetValue("X-RecipeManager-Maintenance-Secret", out var supplied)
            || supplied.Count != 1 || supplied[0] is not { Length: >= 32 and <= 1024 } secret) return false;
        return CryptographicOperations.FixedTimeEquals(_secretHash!, SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}

public sealed record MaintenanceAccount(string Email);
public sealed record MaintenancePassword(string Email, string NewPassword);

public static class MaintenanceEndpoints
{
    public static void ConfigureMaintenance(this WebApplicationBuilder builder, bool generating)
    {
        var path = builder.Configuration["Maintenance:SocketPath"];
        if (generating || string.IsNullOrWhiteSpace(path)) { builder.Services.AddSingleton(new MaintenanceAccess()); return; }
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Host-local maintenance requires the Linux container or a Unix host.");
        if (!Path.IsPathFullyQualified(path) || Encoding.UTF8.GetByteCount(path) > 100)
            throw new InvalidOperationException("Maintenance socket needs an absolute path up to 100 UTF-8 bytes.");
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
            || File.GetUnixFileMode(directory) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            throw new InvalidOperationException("Maintenance socket directory must already exist, be private (0700), and not be a symlink.");
        if (File.Exists(path)) throw new InvalidOperationException("Maintenance socket path already exists. Stop the prior host before removing a stale socket.");
        var secretFile = builder.Configuration["Maintenance:SecretFile"] ?? throw new InvalidOperationException("Configure the mounted maintenance secret file.");
        var secret = SecretConfiguration.ReadSecret(secretFile);
        if (secret.Length is < 32 or > 1024) throw new InvalidOperationException("Maintenance secret must contain 32–1024 characters.");
        var addressText = builder.Configuration["Server:PublicAddress"] ?? throw new InvalidOperationException("Configure Server:PublicAddress when enabling the private listener.");
        if (!Uri.TryCreate(addressText, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https")
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 || address.AbsolutePath != "/")
            throw new InvalidOperationException("Public bind address must be an HTTP/HTTPS origin.");
        IPAddress? ip = null;
        if (address.Host != "localhost" && !IPAddress.TryParse(address.Host, out ip))
            throw new InvalidOperationException("Public bind address must use localhost or a numeric IP address.");
        builder.Services.AddSingleton(new MaintenanceAccess(path, secret));
        builder.WebHost.UseSockets(options => options.CreateBoundListenSocket = endpoint =>
        {
            var socket = SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
            try
            {
                // Set mode after bind creates the filesystem node but BEFORE the transport listens.
                if (endpoint is UnixDomainSocketEndPoint && !OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return socket;
            }
            catch { socket.Dispose(); throw; }
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenUnixSocket(path);
            void ConfigurePublic(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen)
            {
                if (address.Scheme == "https") listen.UseHttps();
            }
            if (address.Host == "localhost") options.ListenLocalhost(address.Port, ConfigurePublic);
            else options.Listen(ip!, address.Port, ConfigurePublic);
        });
    }

    public static void UseMaintenanceBoundary(this WebApplication app)
    {
        var access = app.Services.GetRequiredService<MaintenanceAccess>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/__maintenance"))
            {
                // Authorize before endpoint body binding; neither Host nor forwarded headers suffice.
                if (!access.Authorized(context)) { context.Response.StatusCode = 404; return; }
            }
            else if (access.IsPrivateConnection(context)) { context.Response.StatusCode = 404; return; }
            await next(context);
        });
    }

    public static void MapMaintenanceEndpoints(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<MaintenanceAccess>().Enabled) return;
        var group = app.MapGroup("/__maintenance").AllowAnonymous().ExcludeFromDescription();
        group.MapGet("/ping", () => TypedResults.Ok(new { ready = true }));
        group.MapPost("/accounts/reset-password", async (MaintenancePassword request, UserManager<ApplicationUser> users, SessionRevocationService revocation, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(AccountRules.Email(request.Email)) ?? throw new ResourceMissingException();
            await revocation.RevokeAsync(user.Id, RevocationCause.PasswordReset, null, newPassword: request.NewPassword, cancellationToken: ct);
            return TypedResults.NoContent();
        });
        group.MapPost("/accounts/deactivate", async (MaintenanceAccount request, UserManager<ApplicationUser> users, SessionRevocationService revocation, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(AccountRules.Email(request.Email)) ?? throw new ResourceMissingException();
            await revocation.RevokeAsync(user.Id, RevocationCause.AccountDeactivation, null, cancellationToken: ct);
            return TypedResults.NoContent();
        });
        group.MapPost("/accounts/rotate-stamp", async (MaintenanceAccount request, UserManager<ApplicationUser> users, SessionRevocationService revocation, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(AccountRules.Email(request.Email)) ?? throw new ResourceMissingException();
            await revocation.RevokeAsync(user.Id, RevocationCause.SecurityStampRotation, null, cancellationToken: ct);
            return TypedResults.NoContent();
        });
    }
}
