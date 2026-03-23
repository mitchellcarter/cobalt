using System.Threading.Channels;
using Cobalt.Shared.Events;
using Cobalt.Watcher.Models;

namespace Cobalt.Watcher.Services;

public sealed class LogTailWorker(
    IConfiguration config,
    ApiEventSender sender,
    WatcherAuthService authService,
    ILogger<LogTailWorker> logger) : BackgroundService
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure we have a valid API token before doing anything else
        var token = await authService.LoadTokenAsync();
        if (token is null)
        {
            logger.LogInformation("No API token found — opening browser for Discord login...");
            token = await authService.AuthenticateAsync(stoppingToken);
            logger.LogInformation("Successfully authenticated with Discord.");
        }

        var logPath = config["LogFilePath"] ?? @"D:\Steam\steamapps\common\Rust\output_log.txt";

        if (!File.Exists(logPath))
        {
            logger.LogWarning("Log file not found: {Path}. Waiting for it to appear...", logPath);
            await WaitForFileAsync(logPath, stoppingToken);
        }

        logger.LogInformation("Watching log file: {Path}", logPath);

        // Post a Connected event to the API for the last server from the log (if not disconnected).
        await RestoreCurrentServerAsync(logPath, stoppingToken);

        var consumerTask = ConsumeAsync(stoppingToken);
        await TailAsync(logPath, stoppingToken);
        await consumerTask;
    }

    // ── File tail ────────────────────────────────────────────────────────────────────────────────

    private async Task WaitForFileAsync(string path, CancellationToken ct)
    {
        while (!File.Exists(path) && !ct.IsCancellationRequested)
            await Task.Delay(5000, ct);
    }

    private async Task TailAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        // Skip existing content — history is loaded from the DB on API startup.
        stream.Seek(0, SeekOrigin.End);

        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter      = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => tcs.TrySetResult();

        while (!ct.IsCancellationRequested)
        {
            await Task.WhenAny(tcs.Task, Task.Delay(2000, ct));
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _channel.Writer.TryWrite(line);
            }
        }

        _channel.Writer.Complete();
    }

    // ── Line dispatch ────────────────────────────────────────────────────────────────────────────

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var line in _channel.Reader.ReadAllAsync(ct))
        {
            var parsed = LogParser.Parse(line);
            if (parsed is null) continue;

            try
            {
                await sender.SendAsync(ToDto(parsed), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error dispatching log line: {Line}", line);
            }
        }
    }

    // ── Server restore ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the entire log on startup to find the last server the player was connected to,
    /// but only restores it if the session was not followed by a disconnect or quit line.
    /// </summary>
    private async Task RestoreCurrentServerAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            ConnectionLine? lastConn   = null;
            var             disconnected = false;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                var parsed = LogParser.Parse(line);
                if (parsed is ConnectionLine conn)              { lastConn = conn; disconnected = false; }
                else if (parsed is DisconnectedLine or QuitLine) { disconnected = true; }
            }

            if (lastConn is null || disconnected) return;

            logger.LogInformation("Restoring server from log: {Ip}:{Port}", lastConn.Ip, lastConn.Port);
            await sender.SendAsync(ToDto(lastConn), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to restore current server from log");
        }
    }

    // ── LogLine → LogEventDto mapping ────────────────────────────────────────────────────────────

    private static LogEventDto ToDto(LogLine line) => line switch
    {
        ConnectionLine c   => new LogEventDto(LogEventType.Connected,    c.Timestamp, Ip: c.Ip, Port: c.Port),
        PlayerKillLine k   => new LogEventDto(LogEventType.PlayerKill,   k.Timestamp, KillerName: k.KillerName, KillerSteamId: k.KillerSteamId),
        OtherKillLine o    => new LogEventDto(LogEventType.OtherKill,    o.Timestamp, Cause: o.Cause),
        DisconnectedLine d => new LogEventDto(LogEventType.Disconnected, d.Timestamp, Reason: d.Reason),
        QuitLine q         => new LogEventDto(LogEventType.Quit,         q.Timestamp),
        _                  => throw new InvalidOperationException($"Unknown log line type: {line.GetType().Name}")
    };
}
