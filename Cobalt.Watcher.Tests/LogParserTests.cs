using Cobalt.Watcher.Models;
using Cobalt.Watcher.Services;
using FluentAssertions;
using Xunit;

namespace Cobalt.Watcher.Tests;

public sealed class LogParserTests
{
    // ── Null / whitespace ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsNull(string? input)
    {
        LogParser.Parse(input!).Should().BeNull();
    }

    [Fact]
    public void Parse_UnrecognizedLine_ReturnsNull()
    {
        LogParser.Parse("2026-03-21T05:28:13.845Z|0x2a7c|Some random log line").Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedTimestamp_ReturnsNull()
    {
        LogParser.Parse("NOTADATE|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)").Should().BeNull();
    }

    // ── ConnectionLine ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ConnectionLine_ReturnsConnectionLine()
    {
        var result = LogParser.Parse("2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)");

        result.Should().BeOfType<ConnectionLine>();
    }

    [Fact]
    public void Parse_ConnectionLine_ParsesIp()
    {
        var result = (ConnectionLine)LogParser.Parse("2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)")!;

        result.Ip.Should().Be("64.40.8.152");
    }

    [Fact]
    public void Parse_ConnectionLine_ParsesPort()
    {
        var result = (ConnectionLine)LogParser.Parse("2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)")!;

        result.Port.Should().Be(28014);
    }

    [Fact]
    public void Parse_ConnectionLine_ParsesTimestamp()
    {
        var result = (ConnectionLine)LogParser.Parse("2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)")!;

        result.Timestamp.Should().Be(DateTimeOffset.Parse("2026-03-21T05:28:13.845Z"));
    }

    // ── PlayerKillLine ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_PlayerKillLine_ReturnsPlayerKillLine()
    {
        var result = LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Spooder-Man (76561199844640850)");

        result.Should().BeOfType<PlayerKillLine>();
    }

    [Fact]
    public void Parse_PlayerKillLine_ParsesKillerName()
    {
        var result = (PlayerKillLine)LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Spooder-Man (76561199844640850)")!;

        result.KillerName.Should().Be("Spooder-Man");
    }

    [Fact]
    public void Parse_PlayerKillLine_ParsesSteamId()
    {
        var result = (PlayerKillLine)LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Spooder-Man (76561199844640850)")!;

        result.KillerSteamId.Should().Be("76561199844640850");
    }

    [Fact]
    public void Parse_PlayerKillLine_WithSpacesInName_ParsesFullName()
    {
        var result = (PlayerKillLine)LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by John Doe Smith (76561199844640850)")!;

        result.KillerName.Should().Be("John Doe Smith");
    }

    [Fact]
    public void Parse_PlayerKillLine_ParsesTimestamp()
    {
        var result = (PlayerKillLine)LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Spooder-Man (76561199844640850)")!;

        result.Timestamp.Should().Be(DateTimeOffset.Parse("2026-03-21T05:38:55.622Z"));
    }

    // SteamIDs must be exactly 17 digits — anything shorter falls through to OtherKill
    [Fact]
    public void Parse_KillLineWithShortId_ReturnsOtherKillLine()
    {
        var result = LogParser.Parse("2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Hacker (1234567890123456)");

        result.Should().BeOfType<OtherKillLine>();
    }

    // ── OtherKillLine ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_OtherKillLine_ReturnsOtherKillLine()
    {
        var result = LogParser.Parse("2026-03-21T05:34:35.817Z|0x2a7c|You died: killed by Suicide");

        result.Should().BeOfType<OtherKillLine>();
    }

    [Fact]
    public void Parse_OtherKillLine_ParsesCause()
    {
        var result = (OtherKillLine)LogParser.Parse("2026-03-21T05:34:35.817Z|0x2a7c|You died: killed by Suicide")!;

        result.Cause.Should().Be("Suicide");
    }

    [Fact]
    public void Parse_OtherKillLine_WithMultiWordCause_ParsesFullCause()
    {
        var result = (OtherKillLine)LogParser.Parse("2026-03-21T05:34:35.817Z|0x2a7c|You died: killed by Fall Damage")!;

        result.Cause.Should().Be("Fall Damage");
    }

    // ── DisconnectedLine ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_DisconnectedLine_ReturnsDisconnectedLine()
    {
        var result = LogParser.Parse("2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu");

        result.Should().BeOfType<DisconnectedLine>();
    }

    [Fact]
    public void Parse_DisconnectedLine_ParsesReason()
    {
        var result = (DisconnectedLine)LogParser.Parse("2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu")!;

        result.Reason.Should().Be("disconnect");
    }

    [Fact]
    public void Parse_DisconnectedLine_ParsesTimestamp()
    {
        var result = (DisconnectedLine)LogParser.Parse("2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu")!;

        result.Timestamp.Should().Be(DateTimeOffset.Parse("2026-03-21T05:31:52.646Z"));
    }

    // ── QuitLine ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_QuitLine_ReturnsQuitLine()
    {
        var result = LogParser.Parse("2026-03-21T09:20:24.261Z|0x2a7c|Quitting");

        result.Should().BeOfType<QuitLine>();
    }

    [Fact]
    public void Parse_QuitLine_ParsesTimestamp()
    {
        var result = (QuitLine)LogParser.Parse("2026-03-21T09:20:24.261Z|0x2a7c|Quitting")!;

        result.Timestamp.Should().Be(DateTimeOffset.Parse("2026-03-21T09:20:24.261Z"));
    }
}
