namespace Liquidcast.Api.Models;

/// <summary>One point in the listener time-series, written once per minute by
/// MonitorService. Backs the Monitor page's listener history chart.</summary>
public class ListenerSample
{
    public int Id { get; set; }
    public int Listeners { get; set; }
    public DateTime SampleUtc { get; set; } = DateTime.UtcNow;
}
