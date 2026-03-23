using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Cobalt.Watcher.Services;

public sealed class WatcherAuthService(IConfiguration config, ILogger<WatcherAuthService> logger)
{
    private readonly string _authFilePath = config["AuthTokenPath"] ?? "watcher-auth.json";
    private readonly int    _callbackPort = int.Parse(config["CallbackPort"] ?? "7777");
    private readonly string _apiBaseUrl   = (config["ApiBaseUrl"] ?? "http://localhost:5031").TrimEnd('/');

    private string? _currentToken;

    public string? GetCurrentToken() => _currentToken;

    public async Task<string?> LoadTokenAsync()
    {
        if (!File.Exists(_authFilePath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(_authFilePath);
            using var doc = JsonDocument.Parse(json);
            _currentToken = doc.RootElement.GetProperty("ApiToken").GetString();
            return _currentToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read auth token from {Path}", _authFilePath);
            return null;
        }
    }

    public async Task SaveTokenAsync(string token)
    {
        _currentToken = token;
        await File.WriteAllTextAsync(_authFilePath,
            JsonSerializer.Serialize(new { ApiToken = token }));
        logger.LogInformation("API token saved to {Path}", _authFilePath);
    }

    /// <summary>
    /// Opens the browser to the Discord login page and waits for the OAuth callback
    /// on the local HTTP listener. Returns the API token once the user has authenticated.
    /// </summary>
    public async Task<string> AuthenticateAsync(CancellationToken ct)
    {
        var callbackUrl = $"http://localhost:{_callbackPort}/callback/";
        var loginUrl    = $"{_apiBaseUrl}/auth/watcher-login?redirect={Uri.EscapeDataString($"http://localhost:{_callbackPort}/callback")}";
        logger.LogInformation($"Listening at {callbackUrl}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(callbackUrl);
        listener.Start();

        logger.LogInformation("Opening browser for Discord login: {Url}", loginUrl);
        Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
        logger.LogInformation("Waiting for auth callback on port {Port}...", _callbackPort);

        var ctx = await listener.GetContextAsync().WaitAsync(ct);

        var token = ctx.Request.QueryString["token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteResponseAsync(ctx, 400, "Error: no token received. Please try again.");
            throw new InvalidOperationException("Auth callback did not include a token.");
        }

        await WriteResponseAsync(ctx, 200,
            "<html><body style='font-family:sans-serif;padding:2em'>" +
            "<h2>&#x2705; Authenticated!</h2>" +
            "<p>You can close this tab and return to the Watcher.</p>" +
            "</body></html>");

        await SaveTokenAsync(token);
        return token;
    }

    private static async Task WriteResponseAsync(HttpListenerContext ctx, int statusCode, string body)
    {
        ctx.Response.StatusCode  = statusCode;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
}
