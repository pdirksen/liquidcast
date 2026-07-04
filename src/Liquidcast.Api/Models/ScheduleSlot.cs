namespace Liquidcast.Api.Models;

public enum Recurrence
{
    None = 0,
    Daily = 1,
    Weekly = 2
}

public class ScheduleSlot
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }
    /// <summary>UTC start. For recurring slots this is the first occurrence; the time-of-day is reused.</summary>
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public Recurrence Recurrence { get; set; } = Recurrence.None;
    /// <summary>True = hard cut on slot boundary (flush queue). False = let current track crossfade out.</summary>
    public bool HardCut { get; set; }
    /// <summary>Repeat the playlist for the whole slot if it is shorter than the slot duration.</summary>
    public bool Loop { get; set; } = true;
}
