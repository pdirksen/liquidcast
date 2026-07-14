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
            return Results.Ok(await query.OrderByDescending(t => t.UploadedAt).ToListAsync());
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
                    results.Add(new { file.FileName, track = r.Track, duplicate = r.AlreadyExisted });
                }
                catch (InvalidOperationException ex)
                {
                    results.Add(new { file.FileName, error = ex.Message });
                }
            }
            return Results.Ok(results);
        }).DisableAntiforgery();

        g.MapPost("/rescan", async (TrackService svc, CancellationToken ct) =>
        {
            var r = await svc.SyncFromDiskAsync(ct);
            return Results.Ok(new { added = r.Added, updated = r.Updated });
        });

        g.MapPost("/clear", async (TrackService svc, CancellationToken ct) =>
        {
            await svc.ClearAllAsync(ct);
            var r = await svc.SyncFromDiskAsync(ct);
            return Results.Ok(new { added = r.Added, updated = r.Updated });
        });

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
}
