using Liquidcast.Api.Hubs;
using Liquidcast.Api.Models;
using Liquidcast.Api.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Services;

/// <summary>Polls Icecast + Liquidsoap health and broadcasts a live snapshot over SignalR.</summary>
public class MonitorService : BackgroundService
{
    /// <summary>Listener samples older than this are pruned (covers the "month" chart range + margin).</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(40);
    /// <summary>Play-history rows are kept a year (stats page "year" range).</summary>
    private static readonly TimeSpan PlayRetention = TimeSpan.FromDays(365);

    private readonly IcecastClient _icecast;
    private readonly StreamState _state;
    private readonly IHubContext<MonitorHub> _hub;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MonitorService> _log;

    public MonitorService(IcecastClient icecast, StreamState state, IHubContext<MonitorHub> hub,
        IServiceScopeFactory scopes, ILogger<MonitorService> log)
    {
        _icecast = icecast;
        _state = state;
        _hub = hub;
        _scopes = scopes;
        _log = log;
    }

    private bool? _prevIcecast;
    private bool? _prevLiquidsoap;
    private int _prevListeners = -1;
    private DateTime _lastSampleMinute = DateTime.MinValue;
    private DateTime _lastPruneHour = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var stats = await _icecast.FetchAsync(ct);
                _state.IcecastConnected = stats.MountConnected;
                _state.Listeners = stats.Listeners;
                _state.UpdatedUtc = DateTime.UtcNow;
                LogTransitions();
                await _hub.Clients.All.SendAsync("snapshot", _state.Snapshot(), ct);
                await SampleListenersAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Monitor tick error"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch { break; }
        }
    }

    /// <summary>Persists one listener sample per wall-clock minute (the 2s poll is too dense to store),
    /// and prunes rows past the retention window once per hour.</summary>
    private async Task SampleListenersAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        if (minute == _lastSampleMinute) return;
        _lastSampleMinute = minute;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ListenerSamples.Add(new ListenerSample { Listeners = _state.Listeners, SampleUtc = minute });

        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        if (hour != _lastPruneHour)
        {
            _lastPruneHour = hour;
            var cutoff = now - Retention;
            await db.ListenerSamples.Where(s => s.SampleUtc < cutoff).ExecuteDeleteAsync(ct);
            var playCutoff = now - PlayRetention;
            await db.PlayHistory.Where(p => p.StartedUtc < playCutoff).ExecuteDeleteAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Logs only when a watched state actually changes, to keep the log readable.</summary>
    private void LogTransitions()
    {
        if (_state.LiquidsoapUp != _prevLiquidsoap)
        {
            _log.LogInformation("Liquidsoap is {State}", _state.LiquidsoapUp ? "UP" : "DOWN");
            _prevLiquidsoap = _state.LiquidsoapUp;
        }
        if (_state.IcecastConnected != _prevIcecast)
        {
            if (_state.IcecastConnected)
                _log.LogInformation("Icecast mount connected");
            else
                _log.LogWarning("Icecast mount offline — source not connected or admin stats unreachable");
            _prevIcecast = _state.IcecastConnected;
        }
        if (_state.Listeners != _prevListeners)
        {
            _log.LogInformation("Listeners: {Listeners}", _state.Listeners);
            _prevListeners = _state.Listeners;
        }
    }
}
