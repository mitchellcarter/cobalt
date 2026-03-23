using Cobalt.Watcher.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cobalt.Watcher.Tests;

public sealed class WatcherAuthServiceTests : IDisposable
{
    private readonly string _tokenFile;
    private readonly WatcherAuthService _sut;

    public WatcherAuthServiceTests()
    {
        _tokenFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        _sut = Build(_tokenFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tokenFile))
            File.Delete(_tokenFile);
    }

    private static WatcherAuthService Build(string tokenPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthTokenPath"] = tokenPath,
                ["CallbackPort"]  = "7777",
                ["ApiBaseUrl"]    = "http://localhost:5031"
            })
            .Build();

        return new WatcherAuthService(config, NullLogger<WatcherAuthService>.Instance);
    }

    // ── GetCurrentToken ───────────────────────────────────────────────────────

    [Fact]
    public void GetCurrentToken_Initially_ReturnsNull()
    {
        _sut.GetCurrentToken().Should().BeNull();
    }

    // ── LoadTokenAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadTokenAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var result = await _sut.LoadTokenAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadTokenAsync_WhenFileDoesNotExist_CurrentTokenRemainsNull()
    {
        await _sut.LoadTokenAsync();

        _sut.GetCurrentToken().Should().BeNull();
    }

    [Fact]
    public async Task LoadTokenAsync_WhenFileHasValidToken_ReturnsToken()
    {
        await File.WriteAllTextAsync(_tokenFile, """{"ApiToken":"abc123"}""");

        var result = await _sut.LoadTokenAsync();

        result.Should().Be("abc123");
    }

    [Fact]
    public async Task LoadTokenAsync_WhenFileHasValidToken_SetsCurrentToken()
    {
        await File.WriteAllTextAsync(_tokenFile, """{"ApiToken":"abc123"}""");

        await _sut.LoadTokenAsync();

        _sut.GetCurrentToken().Should().Be("abc123");
    }

    [Fact]
    public async Task LoadTokenAsync_WhenFileIsMalformedJson_ReturnsNull()
    {
        await File.WriteAllTextAsync(_tokenFile, "not json at all");

        var result = await _sut.LoadTokenAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadTokenAsync_WhenApiTokenPropertyIsMissing_ReturnsNull()
    {
        await File.WriteAllTextAsync(_tokenFile, """{"SomeOtherKey":"value"}""");

        var result = await _sut.LoadTokenAsync();

        result.Should().BeNull();
    }

    // ── SaveTokenAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveTokenAsync_SetsCurrentToken()
    {
        await _sut.SaveTokenAsync("my-token");

        _sut.GetCurrentToken().Should().Be("my-token");
    }

    [Fact]
    public async Task SaveTokenAsync_WritesTokenToFileThatCanBeLoadedBack()
    {
        await _sut.SaveTokenAsync("my-token");

        var other = Build(_tokenFile);
        var loaded = await other.LoadTokenAsync();

        loaded.Should().Be("my-token");
    }

    [Fact]
    public async Task SaveTokenAsync_OverwritesPreviousToken()
    {
        await _sut.SaveTokenAsync("first-token");
        await _sut.SaveTokenAsync("second-token");

        _sut.GetCurrentToken().Should().Be("second-token");

        var loaded = await Build(_tokenFile).LoadTokenAsync();
        loaded.Should().Be("second-token");
    }
}
