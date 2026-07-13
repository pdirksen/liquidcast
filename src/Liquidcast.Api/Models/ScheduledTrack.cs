namespace Liquidcast.Api.Models;

/// <summary>
/// One track scheduled at a fixed clock time on one of the five timeline lines.
/// End time is never stored — it is derived from the track duration and cue points.
/// </summary>
public class ScheduledTrack
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>0 = Fallback line (lowest priority), 1..4 = Priority 1 (highest) .. Priority 4.</summary>
    public int Line { get; set; }

    public DateTime StartUtc { get; set; }

    /// <summary>When true, this entry hard-cuts whatever is on air at StartUtc.</summary>
    public bool Override { get; set; }

    // Snapshot of the playlist item's cue/crossfade taken at drop time; null = track defaults.
    public double? CueInSec { get; set; }
    public double? CueOutSec { get; set; }
    public double? CrossfadeSec { get; set; }
}
