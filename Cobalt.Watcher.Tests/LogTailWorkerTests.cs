using System.Net;
using System.Text.Json;
using Cobalt.Shared.Events;
using Cobalt.Watcher.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cobalt.Watcher.Tests;

public sealed class LogTailWorkerTests : IDisposable
{
    private readonly string _tokenFile;
    private readonly string _logFile;
    private readonly CapturingHttpHandler _handler;
    private readonly LogTailWorker _sut;

    public LogTailWorkerTests()
    {
        _tokenFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _logFile   = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        File.WriteAllText(_tokenFile, """{"ApiToken":"test-token"}""");
        File.WriteAllText(_logFile, string.Empty);

        _handler = new CapturingHttpHandler();

        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:5031") };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LogFilePath"]   = _logFile,
                ["AuthTokenPath"] = _tokenFile,
                ["CallbackPort"]  = "7777",
                ["ApiBaseUrl"]    = "http://localhost:5031"
            })
            .Build();

        var authService = new WatcherAuthService(config, NullLogger<WatcherAuthService>.Instance);
        var sender      = new ApiEventSender(new FakeHttpClientFactory(httpClient), authService, NullLogger<ApiEventSender>.Instance);

        _sut = new LogTailWorker(config, sender, authService, NullLogger<LogTailWorker>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (File.Exists(_tokenFile)) File.Delete(_tokenFile);
        if (File.Exists(_logFile))   File.Delete(_logFile);
    }

    /// <summary>
    /// Writes log content, starts the worker, waits long enough for RestoreCurrentServerAsync
    /// to complete (it runs before TailAsync starts), then stops the worker and returns
    /// any events that were sent to the fake HTTP handler.
    /// </summary>
    private async Task<List<LogEventDto>> RunAndCollectAsync(string logContent)
    {
        await File.WriteAllTextAsync(_logFile, logContent);
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await _sut.StopAsync(CancellationToken.None);
        return _handler.CapturedEvents;
    }

    // ── RestoreCurrentServerAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RestoreCurrentServer_WhenActiveConnection_SendsConnectedEvent()
    {
        const string log = "2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)";

        var events = await RunAndCollectAsync(log);

        events.Should().ContainSingle(e => e.EventType == LogEventType.Connected);
    }

    [Fact]
    public async Task RestoreCurrentServer_WhenActiveConnection_EventHasCorrectIpAndPort()
    {
        const string log = "2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)";

        var events = await RunAndCollectAsync(log);

        var ev = events.Should().ContainSingle().Subject;
        ev.Ip.Should().Be("64.40.8.152");
        ev.Port.Should().Be(28014);
    }

    [Fact]
    public async Task RestoreCurrentServer_WhenConnectionFollowedByDisconnect_SendsNoEvents()
    {
        const string log = """
            2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)
            2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu
            """;

        var events = await RunAndCollectAsync(log);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreCurrentServer_WhenConnectionFollowedByQuit_SendsNoEvents()
    {
        const string log = """
            2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)
            2026-03-21T09:20:24.261Z|0x2a7c|Quitting
            """;

        var events = await RunAndCollectAsync(log);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreCurrentServer_WhenLogIsEmpty_SendsNoEvents()
    {
        var events = await RunAndCollectAsync(string.Empty);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreCurrentServer_WithMultipleConnections_UsesLastActiveConnection()
    {
        const string log = """
            2026-03-21T05:00:00.000Z|0x2a7c|Connecting: 1.2.3.4:28015 (Raknet)
            2026-03-21T05:01:00.000Z|0x2a7c|Disconnected (disconnect) - returning to main menu
            2026-03-21T05:02:00.000Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)
            """;

        var events = await RunAndCollectAsync(log);

        var ev = events.Should().ContainSingle().Subject;
        ev.Ip.Should().Be("64.40.8.152");
        ev.Port.Should().Be(28014);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public List<LogEventDto> CapturedEvents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var json = await request.Content.ReadAsStringAsync(CancellationToken.None);
                var dto  = JsonSerializer.Deserialize<LogEventDto>(json, JsonOptions);
                if (dto is not null) CapturedEvents.Add(dto);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
