using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Services;

/// <summary>
/// Clock loop that decides which scheduled track should be on air right now and feeds the
/// persistent Liquidsoap queue just-in-time. Never restarts Liquidsoap; an empty
/// queue simply falls through to the in-script fallback/silence.
///
/// Winner resolution per tick, among entries whose [start, start+duration) covers now:
///  1. any active Override entry → the latest-started override wins (hard cut at its start);
///  2. otherwise the best line: Priority 1 &gt; 2 &gt; 3 &gt; 4 &gt; Fallback line;
///  3. nobody → queue drains → script-level fallback.
/// Server-side validation guarantees a non-override entry never starts strictly inside
/// another entry's interval, which keeps the transitions here simple.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly LiquidsoapClient _ls;
    private readonly RuntimeConfig _cfg;
    private readonly StreamState _state;
    private readonly ILogger<SchedulerService> _log;

    private ScheduledTrack? _current;                      // entry we consider on air (snapshot incl. Track)
    private (int EntryId, DateTime Boundary, string? Rid)? _next; // pre-pushed upcoming segment
    private readonly Queue<PushItem> _pushed = new();      // tracks pushed, in play order (for meta bookkeeping)
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

    private record PushItem(string Uri, string? Title, string? Artist, double DurationSec, int TrackId, int Line);

    /// <summary>Lead time for queueing the next adjacent entry so crossfades have material.</summary>
    private double PrepushSec => Math.Max(20, _cfg.Settings.DefaultCrossfadeSec + 5);

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
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Small indexed window: everything that can be active now plus the near future.
        var entries = await db.ScheduledTracks.AsNoTracking()
            .Include(e => e.Track)
            .Where(e => e.StartUtc > now.AddDays(-1) && e.StartUtc < now.AddHours(1))
            .ToListAsync(ct);
        entries.RemoveAll(e => e.Track is null || !File.Exists(e.Track.StoredPath));

        await CancelStalePrepushAsync(entries, ct);

        var active = entries.Where(e => ScheduleMath.IsActive(e, now)).ToList();
        var winner = ResolveWinner(active);
        await ApplyWinnerAsync(winner, now, ct);
        await UpdateNextAsync(db, now, ct);
        await UpdateOnAirAsync(db, ct);
        if (_current is not null)
            await PrepushNextAsync(entries, now, ct);
    }

    private static ScheduledTrack? ResolveWinner(List<ScheduledTrack> active)
    {
        var overrides = active.Where(e => e.Override).ToList();
        if (overrides.Count > 0)
            return overrides.OrderByDescending(e => e.StartUtc)
                .ThenBy(ScheduleMath.PriorityKey).First(); // a later override interrupts an earlier one
        return active.OrderBy(ScheduleMath.PriorityKey)
            .ThenByDescending(e => e.StartUtc).FirstOrDefault();
    }

    private async Task ApplyWinnerAsync(ScheduledTrack? winner, DateTime now, CancellationToken ct)
    {
        if (winner is null)
        {
            if (_current is not null)
            {
                // Winner gone before its natural end means the playing entry was deleted or
                // moved — stop it. A natural end just lets the queue drain to fallback.
                if (now < ScheduleMath.EndUtc(_current))
                    await FlushQueueAsync(ct);
                _log.LogInformation("Entry {Id} off air — no active entry, falling back", _current.Id);
                _current = null;
                _state.CurrentEntryId = null;
                _state.CurrentLine = null;
            }
            _state.FallbackActive = true;
            return;
        }

        _state.FallbackActive = false;

        if (_current?.Id == winner.Id)
        {
            _current = winner; // refresh snapshot (track metadata / cue edits)
            return;
        }

        // Winner changed. If we already pre-pushed this segment, just promote it.
        if (_next is { } n && n.EntryId == winner.Id)
        {
            _next = null;
        }
        else
        {
            // Cutting into something still playing (override start, or the previous entry
            // was deleted/moved while a successor is active) → hard cut.
            bool cutting = _current is not null && now < ScheduleMath.EndUtc(_current);
            if (cutting || _next is not null)
                await FlushQueueAsync(ct);
            // Late join (gap start, app restart, resume after an override) → shift the cue-in.
            var cueShift = Math.Max(0, (now - winner.StartUtc).TotalSeconds);
            await PushAsync(winner, cueShift, ct);
        }

        _current = winner;
        _state.CurrentEntryId = winner.Id;
        _state.CurrentLine = winner.Line;
        _log.LogInformation("Entry {Id} on air → '{Title}' (line {Line}{Ovr})",
            winner.Id, winner.Track?.Title ?? winner.Track?.FileName, winner.Line,
            winner.Override ? ", override" : "");
    }

    /// <summary>Queues the entry that wins at the current segment's end, shortly before the
    /// boundary, so Liquidsoap can crossfade. Gaps queue nothing — the stream falls back.</summary>
    private async Task PrepushNextAsync(List<ScheduledTrack> entries, DateTime now, CancellationToken ct)
    {
        if (_next is not null) return;

        var curEnd = ScheduleMath.EndUtc(_current!);
        // An upcoming override cuts this segment short; its start is a hard cut, not a crossfade.
        bool overrideAhead = entries.Any(e =>
            e.Override && e.Id != _current!.Id && e.StartUtc > now && e.StartUtc < curEnd);
        if (overrideAhead) return;
        if ((curEnd - now).TotalSeconds > PrepushSec) return;

        var at = curEnd.AddMilliseconds(1);
        var activeAt = entries.Where(e => e.Id != _current!.Id && ScheduleMath.IsActive(e, at)).ToList();
        var next = ResolveWinner(activeAt);
        if (next is null || next.Override) return;

        // Resume support: an entry that started earlier (and was overridden) re-enters mid-track.
        var cueShift = Math.Max(0, (curEnd - next.StartUtc).TotalSeconds);
        var rid = await PushAsync(next, cueShift, ct);
        _next = (next.Id, curEnd, rid);
    }

    /// <summary>Drops the pre-pushed request again if its entry was deleted or moved before it started.</summary>
    private async Task CancelStalePrepushAsync(List<ScheduledTrack> entries, CancellationToken ct)
    {
        if (_next is not { } n) return;
        var entry = entries.FirstOrDefault(e => e.Id == n.EntryId);
        if (entry is not null && ScheduleMath.EndUtc(entry) > n.Boundary && entry.StartUtc <= n.Boundary.AddSeconds(1))
            return; // still valid for that boundary

        _next = null;
        if (n.Rid is not null)
        {
            try { await _ls.CommandAsync($"main.ignore {n.Rid}", ct); }
            catch (Exception ex) { _log.LogDebug(ex, "Could not ignore stale request {Rid}", n.Rid); }
        }
    }

    /// <summary>Surfaces the soonest upcoming entry for the Monitor page — both during a gap
    /// (what breaks the silence) and while an entry plays (what follows, before pre-push).</summary>
    private async Task UpdateNextAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var next = await db.ScheduledTracks.AsNoTracking()
            .Include(e => e.Track)
            .Where(e => e.StartUtc > now)
            .OrderBy(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
        _state.NextStartUtc = next?.StartUtc;
        _state.NextTrackLabel = next is null ? null : Label(next.Track?.Title ?? next.Track?.FileName, next.Track?.Artist);
    }

    /// <summary>Pushes an entry's track, optionally starting <paramref name="cueShiftSec"/> seconds in.</summary>
    private async Task<string?> PushAsync(ScheduledTrack entry, double cueShiftSec, CancellationToken ct)
    {
        var track = entry.Track!;
        var cueIn = (entry.CueInSec is > 0 ? entry.CueInSec.Value : 0) + cueShiftSec;
        var uri = ScriptGenerator.BuildRequestUri(track.StoredPath, track.Title, track.Artist,
            cueIn > 0 ? cueIn : null, entry.CueOutSec,
            entry.CrossfadeSec ?? _cfg.Settings.DefaultCrossfadeSec);

        var resp = await _ls.CommandAsync($"main.push {uri}", ct);
        _pushed.Enqueue(new PushItem(uri, track.Title, track.Artist,
            Math.Max(0, ScheduleMath.EffectiveDurationSec(entry) - cueShiftSec),
            entry.TrackId, entry.Line));

        // main.push answers with the request id — kept so a pre-push can be cancelled.
        var rid = resp?.Trim().Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return rid is not null && rid.All(char.IsDigit) ? rid : null;
    }

    /// <summary>Removes every pending request and skips what is playing (hard cut).</summary>
    private async Task FlushQueueAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _ls.CommandAsync("main.queue", ct);
            var rids = resp.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (rids.Length > 0)
                await _ls.CommandsAsync(rids.Select(r => $"main.ignore {r}"), ct);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Queue flush: could not ignore pending requests"); }
        try { await _ls.CommandAsync("main.skip", ct); } catch { /* best effort */ }
        _pushed.Clear();
        _next = null;
    }

    /// <summary>Reads Liquidsoap's current on-air metadata, updates timing for the monitor
    /// and records the play in history when a pushed track goes on air.</summary>
    private async Task UpdateOnAirAsync(AppDbContext db, CancellationToken ct)
    {
        string onAir;
        try { onAir = (await _ls.CommandAsync("meta.now", ct)).Trim(); }
        catch { return; }

        _state.OnAir = string.IsNullOrWhiteSpace(onAir) ? null : onAir;
        // NOTE: two back-to-back schedules of the same track produce identical labels, so the
        // change detection below misses the boundary and timing bookkeeping stalls for the second.
        if (onAir == _lastOnAir) return;
        // Only a real track becomes "previous" — the silence/fallback gap between two
        // scheduled tracks must not wipe it (single-track entries make gaps common).
        if (!string.IsNullOrWhiteSpace(_lastOnAir))
        {
            _state.Previous = _lastOnAir;
            _state.PreviousStartedUtc = _state.CurrentStartedUtc; // start of the track that just ended
        }
        _lastOnAir = onAir;

        // Track changed: advance our pushed-tracks bookkeeping to find duration.
        PushItem? match = null;
        while (_pushed.Count > 0)
        {
            var head = _pushed.Peek();
            var label = Label(head.Title, head.Artist);
            if (label == onAir || _pushed.Count == 1) { match = _pushed.Dequeue(); break; }
            _pushed.Dequeue(); // skipped/older entry
        }

        _state.CurrentTitle = match?.Title;
        _state.CurrentArtist = match?.Artist;
        _state.CurrentDurationSec = match?.DurationSec ?? 0;
        _state.CurrentStartedUtc = DateTime.UtcNow;
        _state.UpNext = _pushed.Select(i => Label(i.Title, i.Artist)).ToList();

        // A matched push = a scheduled track went on air → one history row. No match
        // (fallback playlist, silence) writes nothing. Inherits the bookkeeping caveats
        // above: an identical consecutive label misses its boundary (no second row), and
        // the count==1 last-resort dequeue can attribute a foreign label to the push.
        if (match is not null)
        {
            db.PlayHistory.Add(new PlayHistory
            {
                TrackId = match.TrackId,
                Title = match.Title,
                Artist = match.Artist,
                DurationSec = match.DurationSec,
                Line = match.Line,
                StartedUtc = DateTime.UtcNow,
            });
            // FK race with a track deleted after push: lose the row, never the tick.
            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex) { _log.LogDebug(ex, "Could not record play history"); }
        }
    }

    private static string Label(string? title, string? artist) =>
        string.IsNullOrEmpty(artist) ? (title ?? "") :
        string.IsNullOrEmpty(title) ? artist! : $"{artist} - {title}";
}
