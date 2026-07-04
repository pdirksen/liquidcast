namespace Liquidcast.Api.Services;

/// <summary>Live snapshot of the stream, shared between scheduler, monitor and SignalR.</summary>
public class StreamState
{
    public bool LiquidsoapUp { get; set; }
    public bool IcecastConnected { get; set; }
    public int Listeners { get; set; }

    public bool FallbackActive { get; set; }
    public bool SchedulerEnabled { get; set; } = true;

    public int? CurrentSlotId { get; set; }
    public int? CurrentPlaylistId { get; set; }
    public string? CurrentPlaylistName { get; set; }

    /// <summary>On-air metadata as reported by Liquidsoap (meta.now).</summary>
    public string? OnAir { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentArtist { get; set; }
    public double CurrentDurationSec { get; set; }
    public DateTime? CurrentStartedUtc { get; set; }

    /// <summary>Label of the track that was on air just before the current one.</summary>
    public string? Previous { get; set; }
    /// <summary>When the previous track started.</summary>
    public DateTime? PreviousStartedUtc { get; set; }
    public List<string> UpNext { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public MonitorSnapshot Snapshot() => new(
        LiquidsoapUp, IcecastConnected, Listeners, FallbackActive, SchedulerEnabled,
        CurrentSlotId, CurrentPlaylistId, CurrentPlaylistName,
        OnAir, CurrentTitle, CurrentArtist, CurrentDurationSec, CurrentStartedUtc,
        Previous, PreviousStartedUtc, UpNext.ToList(), DateTime.UtcNow);
}

public record MonitorSnapshot(
    bool LiquidsoapUp,
    bool IcecastConnected,
    int Listeners,
    bool FallbackActive,
    bool SchedulerEnabled,
    int? CurrentSlotId,
    int? CurrentPlaylistId,
    string? CurrentPlaylistName,
    string? OnAir,
    string? CurrentTitle,
    string? CurrentArtist,
    double CurrentDurationSec,
    DateTime? CurrentStartedUtc,
    string? Previous,
    DateTime? PreviousStartedUtc,
    List<string> UpNext,
    DateTime UpdatedUtc);
