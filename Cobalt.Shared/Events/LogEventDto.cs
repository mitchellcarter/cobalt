namespace Cobalt.Shared.Events;

public sealed record LogEventDto(
    LogEventType EventType,
    DateTimeOffset Timestamp,

    // Connected
    string? Ip = null,
    int? Port = null,

    // PlayerKill
    string? KillerName = null,
    string? KillerSteamId = null,

    // OtherKill
    string? Cause = null,

    // Disconnected
    string? Reason = null
);
