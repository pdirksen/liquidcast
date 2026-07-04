using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Services;

/// <summary>
/// Clock loop that decides what should be on air right now and feeds the
/// persistent Liquidsoap queue just-in-time. Never restarts Liquidsoap; an empty
/// queue simply falls through to the in-script fallback/silence.
/// </summary>
public class SchedulerService : BackgroundService
{
    private const int QueueTarget = 2; // keep this many tracks queued for smooth crossfades

    private readonly IServiceScopeFactory _scopes;
    private readonly LiquidsoapClient _ls;
    private readonly RuntimeConfig _cfg;
    private readonly StreamState _state;
    private readonly ILogger<SchedulerService> _log;

    private string? _activeKey;          // identifies the current slot occurrence
    private DateTime? _activePlaylistUpdatedUtc; // detect live edits to the active playlist
    private List<PushItem> _items = new(); // resolved tracks of the active playlist
    private int _cursor;                   // next item to push
    private bool _slotLoops;
    private readonly Queue<PushItem> _pushed = new(); // tracks pushed, in play order
    private string? _lastOnAir;

    public SchedulerService(IServiceScopeFactory scopes, LiquidsoapClient ls, RuntimeConfig cfg,
        StreamState state, ILogger<SchedulerService> log)
    {
        _scopes = scopes;
        _ls = ls;
        _cfg = cfg;
        _state = state;
        _log = log;
    }

    private record PushItem(string Uri, string? Title, string? Artist, double DurationSec);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Let Liquidsoap come up first.
        try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Scheduler tick error"); }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_state.SchedulerEnabled || !_state.LiquidsoapUp)
            return;

        var now = DateTime.UtcNow;
        await ResolveActiveSlotAsync(now, ct);
        await UpdateOnAirAsync(ct);
        await TopUpQueueAsync(ct);
    }

    /// <summary>Finds the active slot and, on change, (re)loads its playlist and resets the cursor.</summary>
    private async Task ResolveActiveSlotAsync(DateTime now, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var slots = await db.ScheduleSlots.AsNoTracking().ToListAsync(ct);

        ScheduleSlot? active = null;
        DateTime bestStart = DateTime.MinValue;
        foreach (var slot in slots)
        {
            var start = SlotOccurrence.ActiveStart(slot, now);
            if (start is { } s && s >= bestStart) { active = slot; bestStart = s; }
        }

        if (active is null)
        {
            if (_activeKey is not null) ClearSlot();
            await UpdateNextSlotAsync(db, slots, now, ct);
            return;
        }

        _state.NextSlotStartUtc = null;
        _state.NextPlaylistName = null;

        var key = $"{active.Id}@{bestStart:O}";
        if (key == _activeKey)
        {
            // Same occurrence — but reload if the playlist was edited live, so the changes
            // take effect for upcoming pushes without interrupting what is already queued.
            var updated = await db.Playlists.AsNoTracking()
                .Where(p => p.Id == active.PlaylistId)
                .Select(p => (DateTime?)p.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (updated != _activePlaylistUpdatedUtc)
            {
                var edited = await LoadPlaylistAsync(db, active.PlaylistId, ct);
                ApplyItems(active, edited, resetCursor: false);
                _log.LogInformation("Active playlist '{Pl}' edited — reloaded {N} tracks (applies to upcoming tracks)",
                    edited?.Name, _items.Count);
            }
            return;
        }

        bool hadPrevious = _activeKey is not null;
        _activeKey = key;

        var playlist = await LoadPlaylistAsync(db, active.PlaylistId, ct);
        ApplyItems(active, playlist, resetCursor: true);

        // Hard cut: drop whatever is playing so the new slot takes over immediately.
        if (hadPrevious && active.HardCut)
        {
            try { await _ls.CommandAsync("main.skip", ct); } catch { /* best effort */ }
            _pushed.Clear();
        }

        _log.LogInformation("Slot {Slot} active → playlist '{Pl}' ({N} tracks)",
            active.Id, playlist?.Name, _items.Count);
    }

    /// <summary>No slot is active — find the soonest upcoming one across all slots so the
    /// Monitor page has something to show during the gap.</summary>
    private async Task UpdateNextSlotAsync(AppDbContext db, List<ScheduleSlot> slots, DateTime now, CancellationToken ct)
    {
        ScheduleSlot? next = null;
        DateTime bestStart = DateTime.MaxValue;
        foreach (var slot in slots)
        {
            var start = SlotOccurrence.NextStart(slot, now);
            if (start is { } s && s < bestStart) { next = slot; bestStart = s; }
        }

        if (next is null)
        {
            _state.NextSlotStartUtc = null;
            _state.NextPlaylistName = null;
            return;
        }

        _state.NextSlotStartUtc = bestStart;
        _state.NextPlaylistName = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == next.PlaylistId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);
    }

    private static Task<Playlist?> LoadPlaylistAsync(AppDbContext db, int id, CancellationToken ct) =>
        db.Playlists.AsNoTracking()
            .Include(p => p.Items.OrderBy(i => i.Order))
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>Rebuilds the active item list from a playlist. On a live reload the play cursor is
    /// kept (clamped) so playback continues; on a new slot it resets to the start.</summary>
    private void ApplyItems(ScheduleSlot active, Playlist? playlist, bool resetCursor)
    {
        _slotLoops = active.Loop;
        var setting = _cfg.Settings;
        double DefaultCross(double? item) =>
            item ?? playlist?.CrossfadeOverrideSec ?? setting.DefaultCrossfadeSec;

        _items = (playlist?.Items ?? new List<PlaylistItem>())
            .Where(i => i.Track is not null && File.Exists(i.Track!.StoredPath))
            .Select(i => new PushItem(
                ScriptGenerator.BuildRequestUri(i.Track!.StoredPath, i.Track.Title, i.Track.Artist,
                    i.CueInSec, i.CueOutSec, DefaultCross(i.CrossfadeSec)),
                i.Track.Title, i.Track.Artist, i.Track.DurationSec))
            .ToList();

        if (resetCursor || _cursor > _items.Count) _cursor = 0;
        _activePlaylistUpdatedUtc = playlist?.UpdatedAt;

        _state.CurrentSlotId = active.Id;
        _state.CurrentPlaylistId = active.PlaylistId;
        _state.CurrentPlaylistName = playlist?.Name;
        _state.FallbackActive = _items.Count == 0;
    }

    private void ClearSlot()
    {
        _activeKey = null;
        _activePlaylistUpdatedUtc = null;
        _items = new();
        _cursor = 0;
        _state.CurrentSlotId = null;
        _state.CurrentPlaylistId = null;
        _state.CurrentPlaylistName = null;
        _state.FallbackActive = true;
    }

    /// <summary>Pushes tracks until the Liquidsoap queue holds QueueTarget items.</summary>
    private async Task TopUpQueueAsync(CancellationToken ct)
    {
        if (_items.Count == 0) { _state.FallbackActive = true; return; }

        int queued = await QueueCountAsync(ct);
        while (queued < QueueTarget)
        {
            if (_cursor >= _items.Count)
            {
                if (!_slotLoops) break;
                _cursor = 0;
            }
            var item = _items[_cursor++];
            try
            {
                await _ls.CommandAsync($"main.push {item.Uri}", ct);
                _pushed.Enqueue(item);
                queued++;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Push failed");
                break;
            }
        }
        _state.FallbackActive = queued == 0;
    }

    private async Task<int> QueueCountAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _ls.CommandAsync("main.queue", ct);
            if (string.IsNullOrWhiteSpace(resp)) return 0;
            return resp.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch { return 0; }
    }

    /// <summary>Reads Liquidsoap's current on-air metadata and updates timing for the monitor.</summary>
    private async Task UpdateOnAirAsync(CancellationToken ct)
    {
        string onAir;
        try { onAir = (await _ls.CommandAsync("meta.now", ct)).Trim(); }
        catch { return; }

        _state.OnAir = string.IsNullOrWhiteSpace(onAir) ? null : onAir;
        if (onAir == _lastOnAir) return;
        _state.Previous = string.IsNullOrWhiteSpace(_lastOnAir) ? null : _lastOnAir;
        _lastOnAir = onAir;

        // Track changed: advance our pushed-tracks bookkeeping to find duration.
        PushItem? match = null;
        while (_pushed.Count > 0)
        {
            var head = _pushed.Peek();
            var label = Label(head);
            if (label == onAir || _pushed.Count == 1) { match = _pushed.Dequeue(); break; }
            _pushed.Dequeue(); // skipped/older entry
        }

        _state.PreviousStartedUtc = _state.CurrentStartedUtc; // start of the track that just ended
        _state.CurrentTitle = match?.Title;
        _state.CurrentArtist = match?.Artist;
        _state.CurrentDurationSec = match?.DurationSec ?? 0;
        _state.CurrentStartedUtc = DateTime.UtcNow;
        _state.UpNext = _pushed.Select(Label).ToList();
    }

    private static string Label(PushItem i) =>
        string.IsNullOrEmpty(i.Artist) ? (i.Title ?? "") :
        string.IsNullOrEmpty(i.Title) ? i.Artist! : $"{i.Artist} - {i.Title}";
}
