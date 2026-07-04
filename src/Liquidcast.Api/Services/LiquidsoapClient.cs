using System.Net;
using System.Net.Sockets;
using System.Text;
using Liquidcast.Api.Models;

namespace Liquidcast.Api.Services;

/// <summary>
/// Talks to the persistent Liquidsoap process over its server protocol
/// (TCP telnet on Windows, unix domain socket on Linux). One short-lived
/// connection per command batch — Liquidsoap responses end with a line "END".
/// </summary>
public class LiquidsoapClient
{
    private readonly RuntimeConfig _cfg;
    private readonly ILogger<LiquidsoapClient> _log;

    public LiquidsoapClient(RuntimeConfig cfg, ILogger<LiquidsoapClient> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public Task<string> CommandAsync(string command, CancellationToken ct = default)
        => CommandsAsync(new[] { command }, ct).ContinueWith(t => t.Result.FirstOrDefault() ?? "", ct);

    /// <summary>Runs several commands on one connection, returning each response in order.</summary>
    public async Task<List<string>> CommandsAsync(IEnumerable<string> commands, CancellationToken ct = default)
    {
        var results = new List<string>();
        using var socket = CreateSocket();
        try
        {
            await ConnectAsync(socket, ct);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            foreach (var cmd in commands)
            {
                var bytes = Encoding.UTF8.GetBytes(cmd + "\n");
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                results.Add(await ReadUntilEndAsync(stream, ct));
            }
            // Politely close the server session.
            try
            {
                var quit = Encoding.UTF8.GetBytes("quit\n");
                await stream.WriteAsync(quit, ct);
            }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Liquidsoap command failed");
            throw;
        }
        return results;
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
            if (n == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            // Liquidsoap terminates each response with a line consisting of "END".
            var s = sb.ToString();
            if (s.EndsWith("END\r\n") || s.EndsWith("END\n") || s == "END")
            {
                int idx = s.LastIndexOf("END", StringComparison.Ordinal);
                return s[..idx].TrimEnd('\r', '\n');
            }
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
