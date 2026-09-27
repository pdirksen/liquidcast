using Liquidcast.Api.Persistence;
using Liquidcast.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class StatsEndpoints
{
    /// <summary><see cref="ListenerHours"/> is total listening time (Σ listeners × 1 min per sample).</summary>
    public record ListenerStats(string Range, int Peak, DateTime? PeakUtc, double Avg, double ListenerHours,
        List<StatsMath.HourPoint> HourProfile, List<StatsMath.WeekdayPoint> WeekdayProfile);

    public record TopTrack(int? TrackId, string? Title, string? Artist, int Plays, double AirtimeSec);
    public record TopArtist(string? Artist, int Plays, double AirtimeSec);
    /// <summary><see cref="Series"/> is bucketed per <see cref="Bucket"/>: "day" for week/month,
    /// "month" for year (12 bars instead of 365).</summary>
    public record PlayStats(string Range, int TotalPlays, double TotalAirtimeSec, int DistinctTracks,
        string Bucket, List<StatsMath.DayCount> Series, List<TopTrack> TopTracks, List<TopArtist> TopArtists);

    public static void MapStats(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stats").RequireAuthorization();

        // Listener aggregates over the sample window (kept 40 days — no "year" range here).
        g.MapGet("/listeners", async (AppDbContext db, string? range, int? tzOffset, CancellationToken ct) =>
        {
            var days = range == "week" ? 7 : 30; // default month
            var tz = ClampTz(tzOffset);
            var from = DateTime.UtcNow.AddDays(-days);

            var samples = (await db.ListenerSamples.AsNoTracking()
                    .Where(s => s.SampleUtc >= from)
                    .Select(s => new { s.SampleUtc, s.Listeners })
                    .ToListAsync(ct))
                .Select(s => (s.SampleUtc, s.Listeners)).ToList();

            var peak = 0;
            DateTime? peakUtc = null;
            long sum = 0;
            foreach (var (utc, listeners) in samples)
            {
                sum += listeners;
                if (listeners > peak) { peak = listeners; peakUtc = utc; }
            }
            var avg = samples.Count > 0 ? Math.Round((double)sum / samples.Count, 1) : 0;
            // MonitorService stores one sample per minute, so each listener in a sample = 1 listener-minute.
            var listenerHours = Math.Round(sum / 60.0, 1);

            return Results.Ok(new ListenerStats(range == "week" ? "week" : "month", peak, peakUtc, avg, listenerHours,
                StatsMath.HourProfile(samples, tz), StatsMath.WeekdayProfile(samples, tz)));
        });

        // Play-history aggregates. Top lists group by the denormalized Title+Artist so
        // deleted/re-uploaded tracks keep aggregating sensibly; TrackId is a convenience link.
        g.MapGet("/plays", async (AppDbContext db, string? range, int? tzOffset, CancellationToken ct) =>
        {
            var days = range switch { "week" => 7, "year" => 365, _ => 30 };
            var tz = ClampTz(tzOffset);
            var now = DateTime.UtcNow;
            // Window starts at the oldest bucket's local midnight so totals match the bars.
            var local = now.AddMinutes(-tz);
            var from = (range == "year"
                    ? new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11)
                    : DateTime.SpecifyKind(local.Date, DateTimeKind.Utc).AddDays(-(days - 1)))
                .AddMinutes(tz);

            var q = db.PlayHistory.AsNoTracking().Where(p => p.StartedUtc >= from);

            var totalPlays = await q.CountAsync(ct);
            var totalAirtime = await q.SumAsync(p => (double?)p.DurationSec, ct) ?? 0;
            var distinctTracks = await q.Select(p => new { p.Title, p.Artist }).Distinct().CountAsync(ct);

            // Order on group aggregates BEFORE projecting into the record — EF cannot
            // translate an OrderBy on a constructor-bound member like t.Plays.
            var topTracks = await q.GroupBy(p => new { p.Title, p.Artist })
                .OrderByDescending(g2 => g2.Count()).ThenByDescending(g2 => g2.Sum(p => p.DurationSec))
                .Take(10)
                .Select(g2 => new TopTrack(g2.Max(p => p.TrackId), g2.Key.Title, g2.Key.Artist,
                    g2.Count(), g2.Sum(p => p.DurationSec)))
                .ToListAsync(ct);

            var topArtists = await q.Where(p => p.Artist != null && p.Artist != "")
                .GroupBy(p => p.Artist)
                .OrderByDescending(g2 => g2.Count()).ThenByDescending(g2 => g2.Sum(p => p.DurationSec))
                .Take(10)
                .Select(g2 => new TopArtist(g2.Key, g2.Count(), g2.Sum(p => p.DurationSec)))
                .ToListAsync(ct);

            var starts = await q.Select(p => p.StartedUtc).ToListAsync(ct);
            var byMonth = range == "year";
            var series = byMonth
                ? StatsMath.PlaysPerMonth(starts, tz, 12, now)
                : StatsMath.PlaysPerDay(starts, tz, days, now);

            return Results.Ok(new PlayStats(range switch { "week" => "week", "year" => "year", _ => "month" },
                totalPlays, totalAirtime, distinctTracks, byMonth ? "month" : "day", series,
                topTracks, topArtists));
        });
    }

    /// <summary>JS getTimezoneOffset() range is UTC−14h .. UTC+12h.</summary>
    private static int ClampTz(int? tzOffset) => Math.Clamp(tzOffset ?? 0, -840, 720);
}
