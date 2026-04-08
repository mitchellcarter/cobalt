using Cobalt.Shared.Events;
using Cobalt.Watcher.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace Cobalt.Watcher.Tests;

public sealed class ApiEventSenderTests : IDisposable
{
    private readonly string _tempTokenFile;
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly WatcherAuthService _authService;
    private readonly ApiEventSender _sut;

    public ApiEventSenderTests()
    {
        _tempTokenFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        _handler    = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:5031") };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthTokenPath"] = _tempTokenFile,
                ["CallbackPort"]  = "7777",
                ["ApiBaseUrl"]    = "http://localhost:5031"
            })
            .Build();

        _authService = new WatcherAuthService(config, NullLogger<WatcherAuthService>.Instance);

        _sut = new ApiEventSender(
            new FakeHttpClientFactory(_httpClient),
            _authService,
            NullLogger<ApiEventSender>.Instance);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        if (File.Exists(_tempTokenFile))
            File.Delete(_tempTokenFile);
    }

    private static LogEventDto AnyEvent() =>
        new(LogEventType.PlayerKill, DateTimeOffset.UtcNow, KillerName: "Player", KillerSteamId: "12345");

    // ── Routing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_PostsToApiEventsEndpoint()
    {
        await _sut.SendAsync(AnyEvent(), CancellationToken.None);

        _handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/api/events");
        _handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    // ── Authorization header ──────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenTokenPresent_SetsBearerHeader()
    {
        await _authService.SaveTokenAsync("test-token");

        await _sut.SendAsync(AnyEvent(), CancellationToken.None);

        _handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _handler.LastRequest.Headers.Authorization.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task SendAsync_WhenNoToken_DoesNotSetAuthorizationHeader()
    {
        await _sut.SendAsync(AnyEvent(), CancellationToken.None);

        _handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenApiReturnsNonSuccess_DoesNotThrow()
    {
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var act = () => _sut.SendAsync(AnyEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WhenHttpThrows_DoesNotThrow()
    {
        _handler.ExceptionToThrow = new HttpRequestException("Network error");

        var act = () => _sut.SendAsync(AnyEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WhenOperationCancelled_PropagatesException()
    {
        _handler.ExceptionToThrow = new OperationCanceledException();

        var act = () => _sut.SendAsync(AnyEvent(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);
        public Exception? ExceptionToThrow { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;
            return Task.FromResult(ResponseToReturn);
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
