using Liquidcast.Api.Persistence;
using Liquidcast.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class StreamEndpoints
{
    public record SchedulerToggle(bool Enabled);

    public static void MapStream(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stream").RequireAuthorization();

        g.MapGet("/status", (StreamState state) => Results.Ok(state.Snapshot()));

        // Bucketed listener history for the Monitor chart. range = 24h (default) | week | month.
        g.MapGet("/listeners", async (string? range, AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var (window, _) = ListenerHistory.Spec(range ?? "24h");
            var from = now - window;
            var samples = await db.ListenerSamples.AsNoTracking()
                .Where(s => s.SampleUtc >= from)
                .OrderBy(s => s.SampleUtc)
                .Select(s => new { s.SampleUtc, s.Listeners })
                .ToListAsync(ct);
            var series = ListenerHistory.Bucket(
                samples.Select(s => (s.SampleUtc, s.Listeners)).ToList(), range ?? "24h", now);
            return Results.Ok(series);
        });

        g.MapPost("/skip", async (LiquidsoapClient ls, CancellationToken ct) =>
        {
            try { await ls.CommandAsync("main.skip", ct); return Results.Ok(); }
            catch { return Results.Problem("Liquidsoap not reachable."); }
        });

        g.MapPost("/scheduler", (SchedulerToggle body, StreamState state) =>
        {
            state.SchedulerEnabled = body.Enabled;
            return Results.Ok(new { state.SchedulerEnabled });
        });

        g.MapPost("/restart", (LiquidsoapProcess ls) =>
        {
            ls.Restart();
            return Results.Ok();
        });
    }
}
