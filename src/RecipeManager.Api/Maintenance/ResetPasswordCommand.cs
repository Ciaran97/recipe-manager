using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Configuration;

namespace RecipeManager.Api.Maintenance;

public static class ResetPasswordCommand
{
    // This process never resolves Identity/DbContext or starts another host/connection registry.
    public static async Task<int> RunAsync(string[] arguments, IConfiguration configuration)
    {
        if (arguments.Length is < 2 or > 3 || arguments[0] is not ("reset-password" or "deactivate" or "rotate-stamp")
            || (arguments.Length == 3 && (arguments[0] != "reset-password" || arguments[2] != "--password-stdin")))
        {
            Console.Error.WriteLine("Usage: maintenance reset-password|deactivate|rotate-stamp email [--password-stdin]");
            return 2;
        }
        try
        {
            var email = AccountRules.Email(arguments[1]);
            var path = configuration["Maintenance:SocketPath"] ?? throw new InvalidOperationException();
            var secret = SecretConfiguration.ReadSecret(configuration["Maintenance:SecretFile"] ?? throw new InvalidOperationException());
            object payload;
            if (arguments[0] == "reset-password")
            {
                string password;
                if (arguments.Length == 3)
                    password = await Console.In.ReadLineAsync() ?? "";
                else
                {
                    if (Console.IsInputRedirected) { Console.Error.WriteLine("Use --password-stdin for redirected input."); return 2; }
                    Console.Write("New password (input hidden): ");
                    var input = new StringBuilder();
                    while (true)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter) break;
                        if (key.Key == ConsoleKey.Backspace) { if (input.Length > 0) input.Length--; }
                        else if (!char.IsControl(key.KeyChar) && input.Length < 256) input.Append(key.KeyChar);
                    }
                    Console.WriteLine();
                    password = input.ToString();
                }
                AccountRules.Password(password);
                payload = new MaintenancePassword(email, password);
            }
            else payload = new MaintenanceAccount(email);
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                ConnectCallback = async (_, cancellation) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellation); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost"), Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65536 };
            client.DefaultRequestHeaders.Add("X-RecipeManager-Maintenance-Secret", secret);
            using var response = await client.PostAsJsonAsync("/__maintenance/accounts/" + arguments[0], payload);
            if (!response.IsSuccessStatusCode)
            {
                var code = "request_rejected";
                try { var problem = await response.Content.ReadFromJsonAsync<JsonElement>(); code = problem.GetProperty("code").GetString() ?? code; }
                catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { }
                Console.Error.WriteLine($"Maintenance request failed ({code}).");
                return 1;
            }
            Console.WriteLine("Maintenance completed. All affected sessions were revoked.");
            return 0;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or SocketException or OperationCanceledException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine("Maintenance could not complete. Check the running host, private socket, secret file and input.");
            return 1;
        }
    }
}
