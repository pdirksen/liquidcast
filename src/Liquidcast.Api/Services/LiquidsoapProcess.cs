using System.Diagnostics;
using System.Threading.Channels;

namespace Liquidcast.Api.Services;

/// <summary>
/// Owns the single, long-lived Liquidsoap child process: generates the script,
/// starts it before any scheduling, and restarts it with backoff if it dies.
/// Keeping one process alive is what keeps the Icecast connection from dropping.
/// </summary>
public partial class LiquidsoapProcess : IHostedService
{
    private readonly RuntimeConfig _cfg;
    private readonly ScriptGenerator _script;
    private readonly StreamState _state;
    private readonly ILogger<LiquidsoapProcess> _log;

    private Process? _proc;
    private CancellationTokenSource? _supervisorCts;
    private Task? _supervisorTask;

    // Liquidsoap floods stdout at startup (worse at debug level). We must ALWAYS drain that pipe:
    // if we blocked while forwarding a line into _log (e.g. the console logger's queue is full),
    // the OS pipe buffer fills and Liquidsoap deadlocks on its own stdout write before it can even
    // connect to Icecast. So the read callback only does a non-blocking TryWrite here; a separate
    // task does the (potentially blocking) logging. When logging can't keep up we drop lines rather
    // than stall the child — the file log in the .liq keeps the full record.
    private readonly Channel<(string Line, bool Stderr)> _logQueue =
        Channel.CreateBounded<(string, bool)>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    private Task? _logDrainTask;

    public LiquidsoapProcess(RuntimeConfig cfg, ScriptGenerator script, StreamState state,
        ILogger<LiquidsoapProcess> log)
    {
        _cfg = cfg;
        _script = script;
        _state = state;
        _log = log;
    }

    public bool IsRunning => _proc is { HasExited: false };

    /// <summary>Kills the current process; the supervisor regenerates the script and restarts it.
    /// Used after settings changes that are baked into the generated script.</summary>
    public void Restart()
    {
        _log.LogInformation("Restarting Liquidsoap to apply settings");
        KillProcess();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cfg.EnsureDirectories();
        KillOrphanFromPreviousRun();
        _supervisorCts = new CancellationTokenSource();
        _logDrainTask = Task.Run(() => DrainLogQueueAsync(_supervisorCts.Token));
        _supervisorTask = Task.Run(() => SuperviseAsync(_supervisorCts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _supervisorCts?.Cancel();
        _logQueue.Writer.TryComplete();
        KillProcess();
        if (_supervisorTask is not null)
        {
            try { await _supervisorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch { /* shutting down */ }
        }
        if (_logDrainTask is not null)
        {
            try { await _logDrainTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
            catch { /* shutting down */ }
        }
        try { File.Delete(PidFilePath); } catch { /* best effort */ }
    }

    private string PidFilePath => Path.Combine(_cfg.DataPathAbsolute, "liquidsoap.pid");

    /// <summary>Kills a Liquidsoap instance left over from a previous API process that died
    /// without a graceful shutdown. Such an orphan keeps the Icecast mount and the control
    /// port, so the fresh instance would loop on "409 Conflict" while listeners keep hearing
    /// the orphan's (outdated) script.</summary>
    private void KillOrphanFromPreviousRun()
    {
        try
        {
            if (!File.Exists(PidFilePath)) return;
            if (int.TryParse(File.ReadAllText(PidFilePath).Trim(), out var pid))
            {
                using var p = Process.GetProcessById(pid); // throws if already gone
                // Name check: the PID may have been reused by an unrelated process — leave that alone.
                if (p.ProcessName.Contains("liquidsoap", StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("Killing orphaned Liquidsoap (pid {Pid}) from a previous run", pid);
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
            }
        }
        catch { /* already gone — nothing to do */ }
        finally { try { File.Delete(PidFilePath); } catch { /* best effort */ } }
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                WriteScript();
                StartProcess();
                _log.LogInformation("Liquidsoap started (pid {Pid})", _proc!.Id);
                _state.LiquidsoapUp = true;
                backoff = TimeSpan.FromSeconds(1); // reset after a clean start

                await _proc.WaitForExitAsync(ct);
                _state.LiquidsoapUp = false;
                if (ct.IsCancellationRequested) break;
                _log.LogWarning("Liquidsoap exited with code {Code}; restarting in {Backoff}s",
                    _proc.ExitCode, backoff.TotalSeconds);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _state.LiquidsoapUp = false;
                _log.LogError(ex, "Failed to start Liquidsoap; retrying in {Backoff}s", backoff.TotalSeconds);
            }

            try { await Task.Delay(backoff, ct); } catch { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
        KillProcess();
    }

    private void WriteScript()
    {
        File.WriteAllText(_cfg.ScriptPath, _script.Generate());
        // Remove a stale unix socket left by an unclean shutdown.
        if (!OperatingSystem.IsWindows() && File.Exists(_cfg.SocketPath))
        {
            try { File.Delete(_cfg.SocketPath); } catch { /* best effort */ }
        }
    }

    private void StartProcess()
    {
        var binary = ResolveBinary();
        _log.LogInformation("Starting Liquidsoap: binary={Binary}, script={Script}, control={Control}, icecast={Host}:{Port}{Mount}",
            binary, _cfg.ScriptPath, _cfg.Settings.EffectiveControlMode,
            _cfg.Settings.IcecastHost, _cfg.Settings.IcecastPort, _cfg.Settings.IcecastMount);

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _cfg.DataPathAbsolute,
        };
        psi.ArgumentList.Add(_cfg.ScriptPath);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        // Route Liquidsoap's own log lines to the matching .NET level so failures (e.g. a missing
        // encoder, or an Icecast auth reject) are visible without digging into the .liq log file.
        // These callbacks MUST NOT block — see _logQueue. They only enqueue; DrainLogQueueAsync logs.
        proc.OutputDataReceived += (_, e) => EnqueueLine(e.Data);
        proc.ErrorDataReceived += (_, e) => EnqueueLine(e.Data, stderr: true);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _proc = proc;
        // Recorded so the next API start can reap this child if we die without StopAsync.
        try { File.WriteAllText(PidFilePath, proc.Id.ToString()); } catch { /* best effort */ }
    }

    /// <summary>Non-blocking: hands the line to the drain task. Never awaits or blocks, so the
    /// process's stdout/stderr pipe is always drained and Liquidsoap can never deadlock on a write.</summary>
    private void EnqueueLine(string? line, bool stderr = false)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        // Bounded + DropOldest: if logging can't keep up we shed old lines instead of stalling.
        _logQueue.Writer.TryWrite((line, stderr));
    }

    /// <summary>Drains queued Liquidsoap output into the .NET logger off the read-callback thread.
    /// _log.Log may block (e.g. a full console-logger queue); doing it here keeps that back-pressure
    /// off the child process.</summary>
    private async Task DrainLogQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (line, stderr) in _logQueue.Reader.ReadAllAsync(ct))
            {
                var level = ClassifyLevel(line, stderr);
                _log.Log(level, "[ls] {Line}", line);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Maps a Liquidsoap log line to a .NET log level. Liquidsoap lines look like
    /// "2026/07/01 00:19:45 [output.icecast:3] message" where the trailing number is the level
    /// (1=critical … 4=debug). Runtime errors print "Error NN:" / "Uncaught".</summary>
    private static LogLevel ClassifyLevel(string line, bool stderr)
    {
        if (line.Contains("Uncaught", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Error ", StringComparison.Ordinal) ||
            line.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;

        var m = LevelTag().Match(line);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            return n switch
            {
                <= 1 => LogLevel.Critical,
                2 => LogLevel.Warning,
                3 => LogLevel.Information,
                _ => LogLevel.Debug,
            };
        return stderr ? LogLevel.Warning : LogLevel.Debug;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[[^\]]+:(\d)\]")]
    private static partial System.Text.RegularExpressions.Regex LevelTag();

    private string ResolveBinary()
    {
        var configured = _cfg.Settings.LiquidsoapPath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var env = Environment.GetEnvironmentVariable("LIQUIDSOAP_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return OperatingSystem.IsWindows() ? "liquidsoap.exe" : "liquidsoap";
    }

    private void KillProcess()
    {
        try
        {
            if (_proc is { HasExited: false })
                _proc.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        finally { _state.LiquidsoapUp = false; }
    }
}
