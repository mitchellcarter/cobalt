using System.Text.RegularExpressions;
using Cobalt.Watcher.Models;

namespace Cobalt.Watcher.Services;

public static partial class LogParser
{
    // 2026-03-21T05:28:13.845Z|0x2a7c|Connecting: 64.40.8.152:28014 (Raknet)
    [GeneratedRegex(@"^(?<ts>\S+)\|\S+\|Connecting: (?<ip>[\d.]+):(?<port>\d+) \(\w+\)$")]
    private static partial Regex ConnectionRegex();

    // 2026-03-21T05:38:55.622Z|0x2a7c|You died: killed by Spooder-Man (76561199844640850)
    [GeneratedRegex(@"^(?<ts>\S+)\|\S+\|You died: killed by (?<name>.+?) \((?<steamid>\d{17})\)$")]
    private static partial Regex PlayerKillRegex();

    // 2026-03-21T05:34:35.817Z|0x2a7c|You died: killed by Suicide
    [GeneratedRegex(@"^(?<ts>\S+)\|\S+\|You died: killed by (?<cause>.+)$")]
    private static partial Regex OtherKillRegex();

    // 2026-03-21T05:31:52.646Z|0x2a7c|Disconnected (disconnect) - returning to main menu
    [GeneratedRegex(@"^(?<ts>\S+)\|\S+\|Disconnected \((?<reason>[^)]+)\)")]
    private static partial Regex DisconnectedRegex();

    // 2026-03-21T09:20:24.261Z|0x2a7c|Quitting
    [GeneratedRegex(@"^(?<ts>\S+)\|\S+\|Quitting$")]
    private static partial Regex QuitRegex();

    public static LogLine? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var m = ConnectionRegex().Match(line);
        if (m.Success && DateTimeOffset.TryParse(m.Groups["ts"].Value, out var ts1))
            return new ConnectionLine(ts1, m.Groups["ip"].Value, int.Parse(m.Groups["port"].Value));

        m = PlayerKillRegex().Match(line);
        if (m.Success && DateTimeOffset.TryParse(m.Groups["ts"].Value, out var ts2))
            return new PlayerKillLine(ts2, m.Groups["name"].Value, m.Groups["steamid"].Value);

        m = OtherKillRegex().Match(line);
        if (m.Success && DateTimeOffset.TryParse(m.Groups["ts"].Value, out var ts3))
            return new OtherKillLine(ts3, m.Groups["cause"].Value);

        m = DisconnectedRegex().Match(line);
        if (m.Success && DateTimeOffset.TryParse(m.Groups["ts"].Value, out var ts4))
            return new DisconnectedLine(ts4, m.Groups["reason"].Value);

        m = QuitRegex().Match(line);
        if (m.Success && DateTimeOffset.TryParse(m.Groups["ts"].Value, out var ts5))
            return new QuitLine(ts5);

        return null;
    }
}
