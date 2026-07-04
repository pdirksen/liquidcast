using Liquidcast.Api.Data;
using Liquidcast.Api.Models;
using Liquidcast.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public record BackupSettingsDto(bool Enabled, string? TargetPath, string ScheduleTime,
    int KeepCount, DateTime? LastBackupAt);
public record BackupFileDto(string Name, long Size, DateTime CreatedAt);
public record BackupRestoreNameDto(string Name);

public static class BackupEndpoints
{
    public static void MapBackup(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/backup").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, RuntimeConfig cfg) =>
        {
            var b = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync() ?? new BackupSetting();
            return Results.Ok(ToDto(b, cfg));
        });

        g.MapPut("/", async (BackupSettingsDto dto, AppDbContext db, RuntimeConfig cfg) =>
        {
            var b = await db.BackupSettings.FirstOrDefaultAsync();
            if (b is null) { b = new BackupSetting { Id = 1 }; db.BackupSettings.Add(b); }
            b.Enabled = dto.Enabled;
            b.TargetPath = string.IsNullOrWhiteSpace(dto.TargetPath) ? null : dto.TargetPath.Trim();
            b.ScheduleTime = NormalizeTime(dto.ScheduleTime);
            b.KeepCount = dto.KeepCount < 1 ? 1 : dto.KeepCount;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(b, cfg));
        });

        // Manual backup → writes a zip to the target folder and returns its file name.
        g.MapPost("/run", async (AppDbContext db, BackupService backup, RuntimeConfig cfg,
            ILoggerFactory lf, CancellationToken ct) =>
        {
            var b = await db.BackupSettings.FirstOrDefaultAsync();
            if (b is null) { b = new BackupSetting { Id = 1 }; db.BackupSettings.Add(b); }
            var dbPath = ((SqliteConnection)db.Database.GetDbConnection()).DataSource;
            var target = EffectiveTarget(cfg, b.TargetPath);
            try
            {
                var path = await backup.CreateAsync(dbPath, target, ct);
                backup.Prune(target, b.KeepCount);
                b.LastBackupAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { name = Path.GetFileName(path) });
            }
            catch (Exception ex)
            {
                lf.CreateLogger("Backup").LogError(ex, "Manual backup failed.");
                return Results.Problem(detail: "Backup failed.", statusCode: 500);
            }
        });

        // Lists the backup zips present in the target folder, newest first.
        g.MapGet("/list", async (AppDbContext db, RuntimeConfig cfg) =>
        {
            var b = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync();
            var dir = EffectiveTarget(cfg, b?.TargetPath);
            if (!Directory.Exists(dir)) return Results.Ok(Array.Empty<BackupFileDto>());
            var files = new DirectoryInfo(dir).EnumerateFiles($"{BackupService.FilePrefix}*.zip")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new BackupFileDto(f.Name, f.Length, f.CreationTimeUtc))
                .ToArray();
            return Results.Ok(files);
        });

        // Streams a previously created backup zip from the target folder (download).
        g.MapGet("/download", async (string name, AppDbContext db, RuntimeConfig cfg) =>
        {
            if (!IsValidName(name)) return Results.BadRequest(new { error = "Invalid file name." });
            var b = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync();
            var path = Path.Combine(EffectiveTarget(cfg, b?.TargetPath), name);
            return File.Exists(path) ? Results.File(path, "application/zip", name) : Results.NotFound();
        });

        // Restore from an uploaded zip.
        g.MapPost("/restore", async (IFormFile file, AppDbContext db, BackupService backup,
            RuntimeConfig cfg, LiquidsoapProcess ls, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "No file." });
            await using var s = file.OpenReadStream();
            return await RestoreFromAsync(s, db, backup, cfg, ls, lf, ct);
        }).DisableAntiforgery();

        // Restore from a backup already present in the target folder, picked by name.
        g.MapPost("/restore-file", async (BackupRestoreNameDto dto, AppDbContext db, BackupService backup,
            RuntimeConfig cfg, LiquidsoapProcess ls, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!IsValidName(dto.Name)) return Results.BadRequest(new { error = "Invalid file name." });
            var b = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync();
            var path = Path.Combine(EffectiveTarget(cfg, b?.TargetPath), dto.Name);
            if (!File.Exists(path)) return Results.NotFound();
            await using var s = File.OpenRead(path);
            return await RestoreFromAsync(s, db, backup, cfg, ls, lf, ct);
        });
    }

    private static async Task<IResult> RestoreFromAsync(Stream zip, AppDbContext db, BackupService backup,
        RuntimeConfig cfg, LiquidsoapProcess ls, ILoggerFactory lf, CancellationToken ct)
    {
        var dbPath = ((SqliteConnection)db.Database.GetDbConnection()).DataSource;
        try
        {
            await backup.RestoreAsync(zip, dbPath, ct);
            await db.Database.MigrateAsync(ct);

            // Apply restored settings to the running engine.
            var settings = await db.Settings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            cfg.Update(settings);
            cfg.EnsureDirectories();
            ls.Restart();
            return Results.Ok();
        }
        catch (Exception ex)
        {
            lf.CreateLogger("Backup").LogError(ex, "Restore failed.");
            return Results.Problem(detail: "Restore failed. The file may not be a valid backup.",
                statusCode: 400);
        }
    }

    internal static string EffectiveTarget(RuntimeConfig cfg, string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(cfg.DataPathAbsolute, "backups")
            : configured;

    private static BackupSettingsDto ToDto(BackupSetting b, RuntimeConfig cfg) =>
        new(b.Enabled, b.TargetPath ?? EffectiveTarget(cfg, null), b.ScheduleTime, b.KeepCount, b.LastBackupAt);

    private static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('/') && !name.Contains('\\')
        && !name.Contains("..") && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTime(string? time) =>
        TimeOnly.TryParse(time, out var t) ? t.ToString("HH:mm") : "02:00";
}
