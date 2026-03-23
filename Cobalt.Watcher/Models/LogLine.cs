namespace Cobalt.Watcher.Models;

public abstract record LogLine(DateTimeOffset Timestamp);

public sealed record ConnectionLine(DateTimeOffset Timestamp, string Ip, int Port)
    : LogLine(Timestamp);

public sealed record PlayerKillLine(DateTimeOffset Timestamp, string KillerName, string KillerSteamId)
    : LogLine(Timestamp);

public sealed record OtherKillLine(DateTimeOffset Timestamp, string Cause)
    : LogLine(Timestamp);

// 2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu
public sealed record DisconnectedLine(DateTimeOffset Timestamp, string Reason)
    : LogLine(Timestamp);

// 2026-03-21T09:20:24.261Z|0x2a7c|Quitting
public sealed record QuitLine(DateTimeOffset Timestamp)
    : LogLine(Timestamp);
