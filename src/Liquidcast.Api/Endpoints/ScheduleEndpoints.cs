using Liquidcast.Api.Data;
using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class ScheduleEndpoints
{
    public record SlotDto(int PlaylistId, DateTime StartUtc, DateTime EndUtc,
        Recurrence Recurrence, bool HardCut, bool Loop);

    public static void MapSchedule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/schedule").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, DateTime? from, DateTime? to) =>
        {
            var query = db.ScheduleSlots.AsNoTracking().Include(s => s.Playlist).AsQueryable();
            // Non-recurring slots can be windowed; recurring ones are always returned.
            if (from is { } f && to is { } t)
                query = query.Where(s => s.Recurrence != Recurrence.None || (s.StartUtc < t && s.EndUtc > f));
            return Results.Ok(await query.OrderBy(s => s.StartUtc)
                .Select(s => new
                {
                    s.Id, s.PlaylistId, PlaylistName = s.Playlist!.Name,
                    s.StartUtc, s.EndUtc, s.Recurrence, s.HardCut, s.Loop
                }).ToListAsync());
        });

        g.MapPost("/", async (SlotDto dto, AppDbContext db) =>
        {
            var err = Validate(dto);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await OverlapsAsync(db, dto, null))
                return Results.Conflict(new { error = "Overlaps an existing slot." });

            var slot = ToEntity(new ScheduleSlot(), dto);
            db.ScheduleSlots.Add(slot);
            await db.SaveChangesAsync();
            return Results.Created($"/api/schedule/{slot.Id}", slot);
        });

        g.MapPut("/{id:int}", async (int id, SlotDto dto, AppDbContext db) =>
        {
            var slot = await db.ScheduleSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            var err = Validate(dto);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await OverlapsAsync(db, dto, id))
                return Results.Conflict(new { error = "Overlaps an existing slot." });

            ToEntity(slot, dto);
            await db.SaveChangesAsync();
            return Results.Ok(slot);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var slot = await db.ScheduleSlots.FindAsync(id);
            if (slot is null) return Results.NotFound();
            db.ScheduleSlots.Remove(slot);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static string? Validate(SlotDto d)
    {
        if (d.EndUtc <= d.StartUtc) return "End must be after start.";
        if (d.PlaylistId <= 0) return "A playlist is required.";
        return null;
    }

    private static ScheduleSlot ToEntity(ScheduleSlot s, SlotDto d)
    {
        s.PlaylistId = d.PlaylistId;
        // Frontend sends ISO with 'Z' (UTC). Normalize defensively in case an offset/local form arrives.
        s.StartUtc = ToUtc(d.StartUtc);
        s.EndUtc = ToUtc(d.EndUtc);
        s.Recurrence = d.Recurrence;
        s.HardCut = d.HardCut;
        s.Loop = d.Loop;
        return s;
    }

    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc), // assume already UTC
    };

    /// <summary>
    /// Conservative overlap check. Non-recurring vs non-recurring uses real intervals;
    /// anything involving recurrence falls back to a time-of-day overlap check.
    /// </summary>
    private static async Task<bool> OverlapsAsync(AppDbContext db, SlotDto dto, int? excludeId)
    {
        var others = await db.ScheduleSlots.AsNoTracking()
            .Where(s => excludeId == null || s.Id != excludeId).ToListAsync();

        foreach (var o in others)
        {
            if (dto.Recurrence == Recurrence.None && o.Recurrence == Recurrence.None)
            {
                if (dto.StartUtc < o.EndUtc && o.StartUtc < dto.EndUtc) return true;
            }
            else
            {
                // Time-of-day overlap (ignores date) for recurring comparisons.
                var aS = dto.StartUtc.TimeOfDay; var aE = dto.EndUtc.TimeOfDay;
                var bS = o.StartUtc.TimeOfDay; var bE = o.EndUtc.TimeOfDay;
                if (aS < bE && bS < aE) return true;
            }
        }
        return false;
    }
}
