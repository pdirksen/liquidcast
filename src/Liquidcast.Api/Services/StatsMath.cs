namespace Liquidcast.Api.Services;

/// <summary>
/// Pure aggregation helpers for the statistics endpoints (no I/O, mirrors ListenerHistory).
/// All profiles are computed in the CLIENT's local time: <paramref name="tzOffsetMin"/> uses
/// JS getTimezoneOffset() semantics (UTC − local, e.g. −120 for CEST), so
/// local = utc.AddMinutes(-tzOffsetMin). Aggregation is in-memory by design — the source
/// windows are small (≤ ~57k listener rows, ~200k plays/year) and this avoids relying on
/// EF's SQLite translation of date functions.
/// </summary>
public static class StatsMath
{
    /// <summary>Hour of day in local time, 0..23.</summary>
    public record HourPoint(int Hour, double Avg, int Peak);

    /// <summary>Weekday in local time; 0 = Sunday .. 6 = Saturday (.NET DayOfWeek).</summary>
    public record WeekdayPoint(int Weekday, double Avg, int Peak);

    /// <summary>Local calendar day (midnight, Kind Unspecified) with a play count.</summary>
    public record DayCount(DateTime Date, int Count);

    public static List<HourPoint> HourProfile(
        IReadOnlyList<(DateTime SampleUtc, int Listeners)> samples, int tzOffsetMin)
    {
        var sum = new long[24];
        var count = new int[24];
        var peak = new int[24];
        foreach (var (utc, listeners) in samples)
        {
            var h = utc.AddMinutes(-tzOffsetMin).Hour;
            sum[h] += listeners;
            count[h]++;
            if (listeners > peak[h]) peak[h] = listeners;
        }
        return Enumerable.Range(0, 24)
            .Select(h => new HourPoint(h, count[h] > 0 ? Math.Round((double)sum[h] / count[h], 1) : 0, peak[h]))
            .ToList();
    }

    public static List<WeekdayPoint> WeekdayProfile(
        IReadOnlyList<(DateTime SampleUtc, int Listeners)> samples, int tzOffsetMin)
    {
        var sum = new long[7];
        var count = new int[7];
        var peak = new int[7];
        foreach (var (utc, listeners) in samples)
        {
            var d = (int)utc.AddMinutes(-tzOffsetMin).DayOfWeek;
            sum[d] += listeners;
            count[d]++;
            if (listeners > peak[d]) peak[d] = listeners;
        }
        return Enumerable.Range(0, 7)
            .Select(d => new WeekdayPoint(d, count[d] > 0 ? Math.Round((double)sum[d] / count[d], 1) : 0, peak[d]))
            .ToList();
    }

    /// <summary>Continuous series of the last <paramref name="days"/> local calendar days
    /// (ending today), zero-filled so charts show gaps honestly.</summary>
    public static List<DayCount> PlaysPerDay(
        IReadOnlyList<DateTime> startsUtc, int tzOffsetMin, int days, DateTime nowUtc)
    {
        // Strip the Utc kind the arithmetic carried over: these are LOCAL calendar days and
        // must serialize without "Z", or the SPA's Date parsing shifts the label a day in
        // west-of-UTC timezones.
        var today = DateTime.SpecifyKind(nowUtc.AddMinutes(-tzOffsetMin).Date, DateTimeKind.Unspecified);
        var first = today.AddDays(-(days - 1));
        var counts = new int[days];
        foreach (var utc in startsUtc)
        {
            var idx = (int)(utc.AddMinutes(-tzOffsetMin).Date - first).TotalDays;
            if (idx >= 0 && idx < days) counts[idx]++;
        }
        return Enumerable.Range(0, days)
            .Select(i => new DayCount(first.AddDays(i), counts[i]))
            .ToList();
    }
}
