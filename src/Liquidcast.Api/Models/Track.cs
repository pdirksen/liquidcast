namespace Liquidcast.Api.Models;

public class Track
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    /// <summary>Path relative to TracksDir, forward-slashed, including the filename
    /// (e.g. "Shows/Morning/a.mp3"; root files are just "a.mp3"). Source of the folder tree.</summary>
    public string RelativePath { get; set; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public double DurationSec { get; set; }
    public int Bitrate { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlaylistItem> PlaylistItems { get; set; } = new List<PlaylistItem>();
}
