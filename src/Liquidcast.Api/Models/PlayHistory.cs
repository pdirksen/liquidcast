namespace Liquidcast.Api.Models;

/// <summary>One scheduled track that actually went on air. Written by SchedulerService when
/// Liquidsoap's on-air metadata advances to a pushed track. Title/Artist are denormalized
/// snapshots so history survives track deletion; TrackId is a soft link (SetNull).</summary>
public class PlayHistory
{
    public int Id { get; set; }
    public int? TrackId { get; set; }
    public Track? Track { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    /// <summary>Planned effective duration at push time (after cue/late-join shift).</summary>
    public double DurationSec { get; set; }
    /// <summary>Timeline line: 0 = fallback line, 1..4 = priority.</summary>
    public int Line { get; set; }
    public DateTime StartedUtc { get; set; }
}
