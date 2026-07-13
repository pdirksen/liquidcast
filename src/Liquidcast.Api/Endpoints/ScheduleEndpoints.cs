using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Liquidcast.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class ScheduleEndpoints
{
    public record EntryDto(int TrackId, int Line, DateTime StartUtc, bool Override,
        double? CueInSec, double? CueOutSec, double? CrossfadeSec);

    public record LineNameDto(string? Name);

    public static void MapSchedule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/schedule").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, DateTime? from, DateTime? to) =>
        {
            if (from is not { } f || to is not { } t)
                return Results.BadRequest(new { error = "from and to are required." });
            f = ToUtc(f); t = ToUtc(t);

            // End is computed, not stored: fetch with a 24h start margin, refine in memory.
            var margin = f.AddDays(-1);
            var entries = await db.ScheduledTracks.AsNoTracking()
                .Include(e => e.Track)
                .Where(e => e.StartUtc < t && e.StartUtc > margin)
                .OrderBy(e => e.StartUtc)
                .ToListAsync();

            return Results.Ok(entries
                .Where(e => ScheduleMath.EndUtc(e) > f)
                .Select(ToResponse).ToList());
        });

        g.MapPost("/", async (EntryDto dto, AppDbContext db) =>
        {
            var (candidate, error) = await BuildCandidateAsync(db, dto, null);
            if (error is not null) return error;

            // Persist a copy without the Track navigation — the candidate's Track is a
            // detached snapshot and attaching it would make EF insert a duplicate row.
            var entry = Copy(candidate!, new ScheduledTrack());
            db.ScheduledTracks.Add(entry);
            await db.SaveChangesAsync();
            candidate!.Id = entry.Id;
            return Results.Created($"/api/schedule/{entry.Id}", ToResponse(candidate));
        });

        g.MapPut("/{id:int}", async (int id, EntryDto dto, AppDbContext db) =>
        {
            var existing = await db.ScheduledTracks.FirstOrDefaultAsync(e => e.Id == id);
            if (existing is null) return Results.NotFound();

            var (candidate, error) = await BuildCandidateAsync(db, dto, id);
            if (error is not null) return error;

            Copy(candidate!, existing);
            await db.SaveChangesAsync();
            candidate!.Id = id;
            return Results.Ok(ToResponse(candidate));
        });

        // Custom line names: { "1": "Morning Show", ... } — lines without a row use the
        // client's localized default label. PUT with an empty name resets to the default.
        g.MapGet("/lines", async (AppDbContext db) =>
            Results.Ok(await db.ScheduleLines.AsNoTracking()
                .ToDictionaryAsync(l => l.Id, l => l.Name)));

        g.MapPut("/lines/{line:int}", async (int line, LineNameDto dto, AppDbContext db) =>
        {
            if (line is < 0 or > 4)
                return Results.BadRequest(new { error = "Line must be between 0 and 4." });
            var name = (dto.Name ?? "").Trim();
            if (name.Length > 40)
                return Results.BadRequest(new { error = "Name too long (max 40 characters)." });

            var row = await db.ScheduleLines.FindAsync(line);
            if (name.Length == 0) { if (row is not null) db.ScheduleLines.Remove(row); }
            else if (row is null) db.ScheduleLines.Add(new ScheduleLine { Id = line, Name = name });
            else row.Name = name;
            await db.SaveChangesAsync();
            return Results.Ok(new { line, name });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var entry = await db.ScheduledTracks.FindAsync(id);
            if (entry is null) return Results.NotFound();
            db.ScheduledTracks.Remove(entry);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static object ToResponse(ScheduledTrack e) => new
    {
        e.Id,
        e.TrackId,
        Title = e.Track?.Title ?? e.Track?.FileName,
        e.Track?.Artist,
        e.Line,
        e.StartUtc,
        EndUtc = ScheduleMath.EndUtc(e),
        DurationSec = ScheduleMath.EffectiveDurationSec(e),
        e.Override,
        e.CueInSec,
        e.CueOutSec,
        e.CrossfadeSec,
    };

    private static ScheduledTrack Copy(ScheduledTrack from, ScheduledTrack to)
    {
        to.TrackId = from.TrackId;
        to.Line = from.Line;
        to.StartUtc = from.StartUtc;
        to.Override = from.Override;
        to.CueInSec = from.CueInSec;
        to.CueOutSec = from.CueOutSec;
        to.CrossfadeSec = from.CrossfadeSec;
        return to;
    }

    /// <summary>Validates the DTO and returns a detached candidate (Track populated for the
    /// duration math) or an error result. The candidate itself is never tracked by EF.</summary>
    private static async Task<(ScheduledTrack?, IResult?)> BuildCandidateAsync(
        AppDbContext db, EntryDto dto, int? excludeId)
    {
        if (dto.Line is < 0 or > 4)
            return (null, Results.BadRequest(new { error = "Line must be between 0 and 4." }));

        var track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TrackId);
        if (track is null)
            return (null, Results.BadRequest(new { error = "Track not found." }));

        var candidate = new ScheduledTrack
        {
            TrackId = dto.TrackId,
            Track = track,
            Line = dto.Line,
            StartUtc = ToUtc(dto.StartUtc),
            Override = dto.Override,
            CueInSec = dto.CueInSec,
            CueOutSec = dto.CueOutSec,
            CrossfadeSec = dto.CrossfadeSec,
        };

        if (ScheduleMath.EffectiveDurationSec(candidate) <= 0)
            return (null, Results.BadRequest(new { error = "Entry has no audible duration." }));

        var start = candidate.StartUtc;
        var end = ScheduleMath.EndUtc(candidate);
        var margin = start.AddDays(-1);
        var others = await db.ScheduledTracks.AsNoTracking()
            .Include(e => e.Track)
            .Where(e => e.StartUtc < end && e.StartUtc > margin && (excludeId == null || e.Id != excludeId))
            .ToListAsync();

        // An overlap only demands the override flag when the OTHER entry is a plain one —
        // overlapping an override entry is already sanctioned (the override interrupts).
        bool sameLine = false, needsOverride = false;
        foreach (var o in others)
        {
            if (!ScheduleMath.Overlaps(start, end, o.StartUtc, ScheduleMath.EndUtc(o))) continue;
            if (o.Line == candidate.Line) sameLine = true;
            else if (!o.Override) needsOverride = true;
        }

        if (sameLine)
            return (null, Results.Conflict(new { error = "Overlaps an entry on the same line.", code = "same-line" }));
        if (needsOverride && !candidate.Override)
            return (null, Results.Conflict(new { error = "Overlaps another line — enable override to schedule anyway.", code = "needs-override" }));

        return (candidate, null);
    }

    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc), // assume already UTC
    };
}
