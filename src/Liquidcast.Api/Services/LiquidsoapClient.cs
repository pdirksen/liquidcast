using System.Net;
using System.Net.Sockets;
using System.Text;
using Liquidcast.Api.Models;

namespace Liquidcast.Api.Services;

/// <summary>
/// Talks to the persistent Liquidsoap process over its server protocol
/// (TCP telnet on Windows, unix domain socket on Linux). Keeps ONE connection
/// open across commands and reconnects lazily when it drops (e.g. after a
/// Liquidsoap restart). Liquidsoap responses end with a line "END".
///
/// No command-level retry on a broken connection: a command like main.push may
/// already have executed before the failure surfaced, and replaying it would
/// queue the track twice. Callers (the 1s scheduler tick) retry naturally.
/// </summary>
public class LiquidsoapClient : IDisposable
{
    private readonly RuntimeConfig _cfg;
    private readonly ILogger<LiquidsoapClient> _log;

    // The server session is stateful (one reader per connection), so commands
    // from concurrent callers (scheduler tick, skip endpoint) must serialize.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Socket? _socket;
    private NetworkStream? _stream;

    public LiquidsoapClient(RuntimeConfig cfg, ILogger<LiquidsoapClient> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public Task<string> CommandAsync(string command, CancellationToken ct = default)
        => CommandsAsync(new[] { command }, ct).ContinueWith(t => t.Result.FirstOrDefault() ?? "", ct);

    /// <summary>Runs several commands on the shared connection, returning each response in order.</summary>
    public async Task<List<string>> CommandsAsync(IEnumerable<string> commands, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stream = await EnsureConnectedAsync(ct);
            var results = new List<string>();
            foreach (var cmd in commands)
            {
                var bytes = Encoding.UTF8.GetBytes(cmd + "\n");
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                results.Add(await ReadUntilEndAsync(stream, ct));
            }
            return results;
        }
        catch (Exception ex)
        {
            // Any failure (write, read, timeout) may leave unread bytes behind and
            // desync the session — never reuse this connection.
            DropConnection();
            _log.LogDebug(ex, "Liquidsoap command failed");
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await CommandAsync("uptime", ct);
            return !string.IsNullOrWhiteSpace(r);
        }
        catch { return false; }
    }

    private async Task<NetworkStream> EnsureConnectedAsync(CancellationToken ct)
    {
        // Poll+Available==0 detects a remote close (Liquidsoap restarted) before we
        // waste a command on a dead socket; races with an in-flight close still end
        // up in the catch above and reconnect on the next call.
        if (_socket is { Connected: true } s && !(s.Poll(0, SelectMode.SelectRead) && s.Available == 0))
            return _stream!;

        DropConnection();
        var socket = CreateSocket();
        try
        {
            await ConnectAsync(socket, ct);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
        return _stream;
    }

    private void DropConnection()
    {
        try { _stream?.Dispose(); } catch { /* best effort */ }
        try { _socket?.Dispose(); } catch { /* best effort */ }
        _stream = null;
        _socket = null;
    }

    public void Dispose()
    {
        DropConnection();
        _gate.Dispose();
    }

    private Socket CreateSocket() =>
        _cfg.Settings.EffectiveControlMode == ControlMode.UnixSocket
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    private async Task ConnectAsync(Socket socket, CancellationToken ct)
    {
        EndPoint ep = _cfg.Settings.EffectiveControlMode == ControlMode.UnixSocket
            ? new UnixDomainSocketEndPoint(_cfg.SocketPath)
            : new IPEndPoint(IPAddress.Loopback, _cfg.Settings.TelnetPort);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync(ep, timeout.Token);
    }

    private static async Task<string> ReadUntilEndAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[4096];
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            int n = await stream.ReadAsync(buf, timeout.Token);
            if (n == 0) throw new IOException("Liquidsoap closed the control connection.");
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            // Liquidsoap terminates each response with a line consisting of "END".
            var s = sb.ToString();
            if (s.EndsWith("END\r\n") || s.EndsWith("END\n") || s == "END")
            {
                int idx = s.LastIndexOf("END", StringComparison.Ordinal);
                return s[..idx].TrimEnd('\r', '\n');
            }
        }
    }
}
