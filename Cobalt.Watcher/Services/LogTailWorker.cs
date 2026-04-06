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
    // Bounded with Wait so the producer blocks rather than dropping lines when
    // the consumer falls behind (e.g. slow network).  10 000 items is generous
    // headroom; normal bursts are a handful of lines.
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(10_000)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    // Set by the FileSystemWatcher.Created handler (threadpool) and read on the
    // main tail loop.  volatile ensures the write is visible without a lock.
    private volatile bool _reopenPending;

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
        var (stream, reader) = OpenLog(path);
        stream.Seek(0, SeekOrigin.End);

        // Bounded signal channel (capacity 1, DropNewest) coalesces rapid FSW
        // events into a single wake-up.  All three handlers are registered once
        // and never re-added, so there is no handler accumulation.
        var signal = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                FullMode      = BoundedChannelFullMode.DropNewest,
                SingleReader  = true,
                SingleWriter  = false,
            });

        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            InternalBufferSize  = 65536,
            EnableRaisingEvents = true,
        };

        watcher.Changed += (_, _) => signal.Writer.TryWrite(true);

        // File recreation (e.g. game restart): set a flag and signal; the actual
        // dispose/reopen happens on the tail loop, not on this threadpool thread,
        // which prevents races with concurrent ReadLineAsync calls.
        watcher.Created += (_, _) =>
        {
            logger.LogInformation("Log file recreated — will reopen stream");
            _reopenPending = true;
            signal.Writer.TryWrite(true);
        };

        // Re-arm after an internal buffer overflow.
        watcher.Error += (_, e) =>
        {
            logger.LogWarning(e.GetException(), "FileSystemWatcher buffer overflow — re-arming");
            watcher.EnableRaisingEvents = false;
            watcher.EnableRaisingEvents = true;
            signal.Writer.TryWrite(true);
        };

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait for a FSW signal or fall back to the 2-second polling interval.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(2000);
                try   { await signal.Reader.ReadAsync(timeoutCts.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* timeout — fall through to read */ }

                // Reopen the stream here (on the tail loop) rather than on the
                // threadpool handler thread — safe because ReadLineAsync is not
                // running at this point.
                if (_reopenPending)
                {
                    _reopenPending = false;
                    reader.Dispose();
                    stream.Dispose();
                    (stream, reader) = OpenLog(path);
                }

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        await _channel.Writer.WriteAsync(line, ct);
                }
            }
        }
        finally
        {
            _channel.Writer.Complete();
            reader.Dispose();
            stream.Dispose();
        }
    }

    private static (FileStream stream, StreamReader reader) OpenLog(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return (stream, new StreamReader(stream));
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

            ConnectionLine? lastConn     = null;
            var             disconnected = false;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                var parsed = LogParser.Parse(line);
                if (parsed is ConnectionLine conn)               { lastConn = conn; disconnected = false; }
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
