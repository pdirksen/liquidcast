using Liquidcast.Api.Data;
using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class PlaylistEndpoints
{
    public record PlaylistDto(string Name, string? Description, double? CrossfadeOverrideSec);
    public record ItemDto(int TrackId, double? CueInSec, double? CueOutSec, double? CrossfadeSec);

    public static void MapPlaylists(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/playlists").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.Playlists.AsNoTracking()
                .Select(p => new
                {
                    p.Id, p.Name, p.Description, p.CrossfadeOverrideSec, p.UpdatedAt,
                    ItemCount = p.Items.Count,
                    TotalDurationSec = p.Items.Sum(i => i.Track!.DurationSec)
                })
                .OrderBy(p => p.Name).ToListAsync()));

        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var p = await db.Playlists.AsNoTracking()
                .Include(p => p.Items.OrderBy(i => i.Order))
                .ThenInclude(i => i.Track)
                .FirstOrDefaultAsync(p => p.Id == id);
            return p is null ? Results.NotFound() : Results.Ok(p);
        });

        g.MapPost("/", async (PlaylistDto dto, AppDbContext db) =>
        {
            var p = new Playlist
            {
                Name = dto.Name,
                Description = dto.Description,
                CrossfadeOverrideSec = dto.CrossfadeOverrideSec,
            };
            db.Playlists.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/playlists/{p.Id}", p);
        });

        g.MapPut("/{id:int}", async (int id, PlaylistDto dto, AppDbContext db) =>
        {
            var p = await db.Playlists.FindAsync(id);
            if (p is null) return Results.NotFound();
            p.Name = dto.Name;
            p.Description = dto.Description;
            p.CrossfadeOverrideSec = dto.CrossfadeOverrideSec;
            p.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(p);
        });

        // Replace the full ordered item list (drag-and-drop saves the whole timeline).
        g.MapPut("/{id:int}/items", async (int id, List<ItemDto> items, AppDbContext db) =>
        {
            var p = await db.Playlists.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (p is null) return Results.NotFound();

            db.PlaylistItems.RemoveRange(p.Items);
            int order = 0;
            foreach (var it in items)
            {
                p.Items.Add(new PlaylistItem
                {
                    TrackId = it.TrackId,
                    Order = order++,
                    CueInSec = it.CueInSec,
                    CueOutSec = it.CueOutSec,
                    CrossfadeSec = it.CrossfadeSec,
                });
            }
            p.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var saved = await db.Playlists.AsNoTracking()
                .Include(p => p.Items.OrderBy(i => i.Order)).ThenInclude(i => i.Track)
                .FirstAsync(p => p.Id == id);
            return Results.Ok(saved);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var p = await db.Playlists.FindAsync(id);
            if (p is null) return Results.NotFound();
            db.Playlists.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
