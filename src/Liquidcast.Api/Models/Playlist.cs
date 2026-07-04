namespace Liquidcast.Api.Models;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Per-playlist crossfade override in seconds. Null = use global default.</summary>
    public double? CrossfadeOverrideSec { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}

public class PlaylistItem
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }
    public int TrackId { get; set; }
    public Track? Track { get; set; }
    /// <summary>Zero-based position within the playlist.</summary>
    public int Order { get; set; }
    public double? CueInSec { get; set; }
    public double? CueOutSec { get; set; }
    /// <summary>Per-item crossfade override in seconds. Null = use playlist/global default.</summary>
    public double? CrossfadeSec { get; set; }
}
