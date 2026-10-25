namespace Liquidcast.Api.Services;

/// <summary>
/// Pure aggregation helpers for the statistics endpoints (no I/O, mirrors ListenerHistory).
/// All profiles are computed in the CLIENT's time zone, converting each timestamp with the
/// offset in force at that instant — a window spanning a DST switch (CEST ↔ CET) keeps every
/// play in its real local hour/day. Aggregation is in-memory by design — the source
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
        IReadOnlyList<(DateTime SampleUtc, int Listeners)> samples, TimeZoneInfo tz)
    {
        var sum = new long[24];
        var count = new int[24];
        var peak = new int[24];
        foreach (var (utc, listeners) in samples)
        {
            var h = Local(utc, tz).Hour;
            sum[h] += listeners;
            count[h]++;
            if (listeners > peak[h]) peak[h] = listeners;
        }
        return Enumerable.Range(0, 24)
            .Select(h => new HourPoint(h, count[h] > 0 ? Math.Round((double)sum[h] / count[h], 1) : 0, peak[h]))
            .ToList();
    }

    public static List<WeekdayPoint> WeekdayProfile(
        IReadOnlyList<(DateTime SampleUtc, int Listeners)> samples, TimeZoneInfo tz)
    {
        var sum = new long[7];
        var count = new int[7];
        var peak = new int[7];
        foreach (var (utc, listeners) in samples)
        {
            var d = (int)Local(utc, tz).DayOfWeek;
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
        IReadOnlyList<DateTime> startsUtc, TimeZoneInfo tz, int days, DateTime nowUtc)
    {
        var today = Local(nowUtc, tz).Date;
        var first = today.AddDays(-(days - 1));
        var counts = new int[days];
        foreach (var utc in startsUtc)
        {
            var idx = (int)(Local(utc, tz).Date - first).TotalDays;
            if (idx >= 0 && idx < days) counts[idx]++;
        }
        return Enumerable.Range(0, days)
            .Select(i => new DayCount(first.AddDays(i), counts[i]))
            .ToList();
    }

    /// <summary>Continuous series of the last <paramref name="months"/> local calendar months
    /// (ending with the current one), zero-filled. Each Date is the 1st of the month.
    /// Used for the year range, where 365 daily bars are unreadable.</summary>
    public static List<DayCount> PlaysPerMonth(
        IReadOnlyList<DateTime> startsUtc, TimeZoneInfo tz, int months, DateTime nowUtc)
    {
        var local = Local(nowUtc, tz);
        var current = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var first = current.AddMonths(-(months - 1));
        var counts = new int[months];
        foreach (var utc in startsUtc)
        {
            var l = Local(utc, tz);
            var idx = (l.Year - first.Year) * 12 + l.Month - first.Month;
            if (idx >= 0 && idx < months) counts[idx]++;
        }
        return Enumerable.Range(0, months)
            .Select(i => new DayCount(first.AddMonths(i), counts[i]))
            .ToList();
    }

    /// <summary>UTC → wall-clock time in <paramref name="tz"/>, Kind Unspecified: these are LOCAL
    /// calendar values and must serialize without "Z" (or an offset), or the SPA's Date parsing
    /// shifts day labels in west-of-UTC timezones.</summary>
    public static DateTime Local(DateTime utc, TimeZoneInfo tz) => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz),
        DateTimeKind.Unspecified);

    /// <summary>Wall-clock time in <paramref name="tz"/> → UTC. A time skipped by a spring-forward
    /// jump resolves to the first instant after the gap.</summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }
}
