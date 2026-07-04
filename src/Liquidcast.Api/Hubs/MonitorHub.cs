using Liquidcast.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Liquidcast.Api.Hubs;

[Authorize]
public class MonitorHub : Hub
{
    private readonly StreamState _state;

    public MonitorHub(StreamState state) => _state = state;

    public override async Task OnConnectedAsync()
    {
        // Send the current snapshot immediately on connect.
        await Clients.Caller.SendAsync("snapshot", _state.Snapshot());
        await base.OnConnectedAsync();
    }
}
