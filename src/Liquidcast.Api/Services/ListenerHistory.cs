namespace Liquidcast.Api.Services;

/// <summary>Pure time-bucketing for the listener history chart. No I/O — unit-testable in isolation.</summary>
public static class ListenerHistory
{
    public record Point(DateTime T, int Avg, int Peak);
    public record Series(string Range, List<Point> Points);

    /// <summary>Window length + bucket size for each supported range keyword.</summary>
    public static (TimeSpan Window, TimeSpan Bucket) Spec(string range) => range switch
    {
        "week" => (TimeSpan.FromDays(7), TimeSpan.FromHours(1)),      // 168 buckets
        "month" => (TimeSpan.FromDays(30), TimeSpan.FromHours(6)),    // 120 buckets
        _ => (TimeSpan.FromHours(24), TimeSpan.FromMinutes(5)),       // 24h → 288 buckets (default)
    };

    /// <summary>
    /// Buckets raw (timestamp, listeners) samples into a fixed grid ending at <paramref name="nowUtc"/>.
    /// Every bucket in the window is emitted (empty buckets → Avg/Peak 0) so the chart has a continuous
    /// x-axis. Bucket timestamp is the bucket's start.
    /// </summary>
    public static Series Bucket(IReadOnlyList<(DateTime SampleUtc, int Listeners)> samples, string range, DateTime nowUtc)
    {
        var (window, bucket) = Spec(range);
        var normalizedRange = range is "week" or "month" ? range : "24h";

        // Align the grid end to the current bucket boundary so points land on stable ticks.
        var ticks = bucket.Ticks;
        var end = new DateTime((nowUtc.Ticks / ticks + 1) * ticks, DateTimeKind.Utc);
        var start = end - window;
        var count = (int)(window.Ticks / ticks);

        var sums = new long[count];
        var counts = new int[count];
        var peaks = new int[count];

        foreach (var (ts, listeners) in samples)
        {
            if (ts < start || ts >= end) continue;
            var idx = (int)((ts.Ticks - start.Ticks) / ticks);
            if (idx < 0 || idx >= count) continue;
            sums[idx] += listeners;
            counts[idx]++;
            if (listeners > peaks[idx]) peaks[idx] = listeners;
        }

        var points = new List<Point>(count);
        for (var i = 0; i < count; i++)
        {
            var avg = counts[i] > 0 ? (int)Math.Round((double)sums[i] / counts[i]) : 0;
            points.Add(new Point(start.AddTicks(i * ticks), avg, peaks[i]));
        }

        return new Series(normalizedRange, points);
    }
}
