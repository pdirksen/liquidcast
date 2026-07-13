using Liquidcast.Api.Models;

namespace Liquidcast.Api.Services;

/// <summary>Shared timing math for scheduled tracks (mirrored client-side in the SPA).</summary>
public static class ScheduleMath
{
    /// <summary>Audible duration of an entry: cue-out (or file end) minus cue-in, never negative.</summary>
    public static double EffectiveDurationSec(ScheduledTrack e)
    {
        var total = e.Track?.DurationSec ?? 0;
        var end = e.CueOutSec is > 0 ? Math.Min(e.CueOutSec.Value, total > 0 ? total : e.CueOutSec.Value) : total;
        var start = e.CueInSec is > 0 ? e.CueInSec.Value : 0;
        return Math.Max(0, end - start);
    }

    public static DateTime EndUtc(ScheduledTrack e) => e.StartUtc.AddSeconds(EffectiveDurationSec(e));

    public static bool IsActive(ScheduledTrack e, DateTime nowUtc) =>
        e.StartUtc <= nowUtc && nowUtc < EndUtc(e);

    /// <summary>Half-open interval overlap — touching entries are adjacent, not overlapping.</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>Playback priority: lower is better. Priority 1..4 → 1..4, Fallback line (0) → 5.</summary>
    public static int PriorityKey(ScheduledTrack e) => e.Line == 0 ? 5 : e.Line;
}
