using Liquidcast.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Liquidcast.Api.Services;

/// <summary>Polls Icecast + Liquidsoap health and broadcasts a live snapshot over SignalR.</summary>
public class MonitorService : BackgroundService
{
    private readonly IcecastClient _icecast;
    private readonly StreamState _state;
    private readonly IHubContext<MonitorHub> _hub;
    private readonly ILogger<MonitorService> _log;

    public MonitorService(IcecastClient icecast, StreamState state, IHubContext<MonitorHub> hub,
        ILogger<MonitorService> log)
    {
        _icecast = icecast;
        _state = state;
        _hub = hub;
        _log = log;
    }

    private bool? _prevIcecast;
    private bool? _prevLiquidsoap;
    private int _prevListeners = -1;

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
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Monitor tick error"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch { break; }
        }
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
