using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Cobalt.Watcher.Services;

public sealed class UpdateCheckService(
    IHttpClientFactory httpClientFactory,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    private static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check immediately on startup, then every 24 hours
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            await CheckAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            var client   = httpClientFactory.CreateClient("GitHub");
            var release  = await client.GetFromJsonAsync<GhRelease>(
                "/repos/mitchellcarter/cobalt/releases/latest", ct);

            if (release is null) return;

            var tag = release.TagName?.TrimStart('v', 'V');
            // Version.TryParse requires at least "major.minor"; pad single-number tags like "42" → "42.0"
            if (!string.IsNullOrWhiteSpace(tag) && !tag.Contains('.')) tag += ".0";
            if (!Version.TryParse(tag, out var latest)) return;

            if (latest <= CurrentVersion)
            {
                logger.LogInformation("Cobalt Watcher is up to date (v{Version})", CurrentVersion);
                return;
            }

            // Print a prominent banner so it's impossible to miss in the console.
            // Width is driven by the longest content line so URLs never overflow the box.
            var line1 = $"  A new version of Cobalt Watcher is available: v{latest}  ";
            var line2 = $"  Current version : v{CurrentVersion}";
            var line3 = $"  Download        : {release.HtmlUrl}";
            var width  = Math.Max(60, Math.Max(line1.Length, Math.Max(line2.Length, line3.Length)) + 2);
            var border = new string('═', width);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine($"╔{border}╗");
            Console.WriteLine($"║{line1.PadRight(width)}║");
            Console.WriteLine($"║{line2.PadRight(width)}║");
            Console.WriteLine($"║{line3.PadRight(width)}║");
            Console.WriteLine($"╚{border}╝");
            Console.WriteLine();
            Console.ResetColor();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Update check failed — will retry in 24 hours");
        }
    }

    private sealed record GhRelease(
        [property: JsonPropertyName("tag_name")]  string? TagName,
        [property: JsonPropertyName("html_url")]  string? HtmlUrl);
}
