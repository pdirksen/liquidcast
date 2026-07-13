namespace Liquidcast.Api.Models;

/// <summary>Custom display name for a timeline line. Id is the line number itself
/// (0 = fallback line, 1..4). Only renamed lines have a row — a missing row means
/// the client shows its localized default label.</summary>
public class ScheduleLine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
