using Liquidcast.Api.Services;

namespace Liquidcast.Api.Endpoints;

public static class StreamEndpoints
{
    public record SchedulerToggle(bool Enabled);

    public static void MapStream(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stream").RequireAuthorization();

        g.MapGet("/status", (StreamState state) => Results.Ok(state.Snapshot()));

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
