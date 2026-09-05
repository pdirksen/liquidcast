using Liquidcast.Api.Persistence;
using Liquidcast.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class TrackEndpoints
{
    public static void MapTracks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tracks").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, string? q) =>
        {
            var query = db.Tracks.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.ToLower();
                query = query.Where(t =>
                    (t.Title != null && t.Title.ToLower().Contains(term)) ||
                    (t.Artist != null && t.Artist.ToLower().Contains(term)) ||
                    t.FileName.ToLower().Contains(term) ||
                    t.RelativePath.ToLower().Contains(term));
            }
            // Slim projection — the SPA never needs hash/absolute path, and at thousands of
            // tracks the full entity roughly doubles the payload.
            return Results.Ok(await query
                .OrderByDescending(t => t.UploadedAt)
                .Select(t => new TrackListDto(t.Id, t.FileName, t.RelativePath, t.Title, t.Artist,
                    t.Album, t.DurationSec, t.Bitrate, t.SizeBytes))
                .ToListAsync());
        });

        // Where each track is already used — the playlist editor marks library rows that are
        // in another playlist and/or on the schedule. Only tracks with at least one use are
        // returned, so the payload stays small on a big library. Entries older than
        // ArchiveAfterDays are archived: they get their own count, not the schedule one.
        g.MapGet("/usage", async (AppDbContext db, RuntimeConfig cfg) =>
        {
            var inPlaylists = await db.PlaylistItems.AsNoTracking()
                .Select(i => new { i.TrackId, i.PlaylistId })
                .Distinct()
                .ToListAsync();
            var cutoff = cfg.ArchiveCutoffUtc;
            var scheduled = await db.ScheduledTracks.AsNoTracking()
                .GroupBy(s => s.TrackId)
                .Select(x => new
                {
                    TrackId = x.Key,
                    Count = x.Count(s => s.StartUtc >= cutoff),
                    Archived = x.Count(s => s.StartUtc < cutoff),
                })
                .ToListAsync();

            var byTrack = new Dictionary<int, (List<int> Playlists, int Scheduled, int Archived)>();
            foreach (var row in inPlaylists)
            {
                if (!byTrack.TryGetValue(row.TrackId, out var e))
                    e = byTrack[row.TrackId] = (new List<int>(), 0, 0);
                e.Playlists.Add(row.PlaylistId);
            }
            foreach (var row in scheduled)
            {
                byTrack.TryGetValue(row.TrackId, out var e);
                byTrack[row.TrackId] = (e.Playlists ?? new List<int>(), row.Count, row.Archived);
            }

            return Results.Ok(byTrack
                .Select(kv => new TrackUsageDto(kv.Key, kv.Value.Playlists, kv.Value.Scheduled, kv.Value.Archived))
                .ToList());
        });

        g.MapPost("/upload", async (HttpRequest request, TrackService svc, RuntimeConfig cfg, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data." });

            var maxUploadBytes = cfg.MaxUploadBytes;
            if (request.ContentLength > maxUploadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = maxUploadBytes;

            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count == 0)
                return Results.BadRequest(new { error = "No files uploaded." });

            var folder = form["folder"].ToString();
            var results = new List<object>();
            foreach (var file in form.Files)
            {
                try
                {
                    await using var stream = file.OpenReadStream();
                    var r = await svc.SaveUploadAsync(stream, file.FileName, folder, ct);
                    results.Add(new { file.FileName, track = r.Track, duplicateOf = r.DuplicateOf?.RelativePath });
                }
                catch (InvalidOperationException ex)
                {
                    results.Add(new { file.FileName, error = ex.Message });
                }
            }
            return Results.Ok(results);
        }).DisableAntiforgery();

        // Scans run in the background (a first import of thousands of files takes minutes);
        // the SPA polls /scan-status for progress and completion.
        g.MapPost("/rescan", (LibraryScanService scans) =>
        {
            scans.StartScan(clearFirst: false);
            return Results.Accepted();
        });

        g.MapPost("/clear", (LibraryScanService scans) =>
        {
            scans.StartScan(clearFirst: true);
            return Results.Accepted();
        });

        g.MapGet("/scan-status", (LibraryScanService scans) => Results.Ok(scans.Status));

        g.MapGet("/folders", (TrackService svc) => Results.Ok(svc.ListFolders()));

        g.MapPost("/folders", (FolderDto dto, TrackService svc) =>
        {
            try { return Results.Ok(new { path = svc.CreateFolder(dto.Path) }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        g.MapDelete("/folders", (string path, TrackService svc) =>
        {
            try { svc.DeleteFolder(path); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        g.MapPost("/{id:int}/move", async (int id, MoveDto dto, TrackService svc, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.MoveTrackAsync(id, dto.Folder, ct)); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (IOException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        g.MapGet("/{id:int}/file", async (int id, AppDbContext db) =>
        {
            var track = await db.Tracks.FindAsync(id);
            if (track is null || !File.Exists(track.StoredPath)) return Results.NotFound();
            return Results.File(track.StoredPath, "audio/mpeg", enableRangeProcessing: true);
        });

        // Full metadata for one track — the schedule dialog shows it for the entry's
        // track, which the schedule payload only carries title/artist for.
        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var t = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return t is null
                ? Results.NotFound()
                : Results.Ok(new TrackDetailDto(t.Id, t.FileName, t.RelativePath, t.Title, t.Artist,
                    t.Album, t.DurationSec, t.Bitrate, t.SizeBytes, t.UploadedAt));
        });

        g.MapDelete("/{id:int}", async (int id, TrackService svc, CancellationToken ct) =>
        {
            try
            {
                return await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });
    }

    private record FolderDto(string? Path);
    private record MoveDto(string? Folder);
    private record TrackListDto(int Id, string FileName, string RelativePath, string? Title,
        string? Artist, string? Album, double DurationSec, int Bitrate, long SizeBytes);
    private record TrackDetailDto(int Id, string FileName, string RelativePath, string? Title,
        string? Artist, string? Album, double DurationSec, int Bitrate, long SizeBytes, DateTime UploadedAt);
    private record TrackUsageDto(int TrackId, List<int> PlaylistIds, int ScheduledCount, int ArchivedCount);
}
