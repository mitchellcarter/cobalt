using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cobalt.Shared.Events;

namespace Cobalt.Watcher.Services;

public sealed class ApiEventSender(
    IHttpClientFactory httpClientFactory,
    WatcherAuthService authService,
    ILogger<ApiEventSender> logger)
{
    public async Task SendAsync(LogEventDto dto, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Api");

            // Inject the current Bearer token per-request so it's always up to date
            var token = authService.GetCurrentToken();
            if (!string.IsNullOrWhiteSpace(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/events", dto, ct);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("API rejected {EventType} event: {Status}", dto.EventType, response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to send {EventType} event to API", dto.EventType);
        }
    }
}
