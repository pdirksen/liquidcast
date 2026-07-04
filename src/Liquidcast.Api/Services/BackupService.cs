using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Liquidcast.Api.Services;

/// <summary>
/// Creates and restores zip backups of the Liquidcast SQLite database (a consistent
/// online-backup snapshot). DB only — track/fallback MP3s are not included.
/// </summary>
public sealed class BackupService
{
    private const string DbEntry = "liquidcast.db";
    private const string ManifestEntry = "manifest.json";
    public const string FilePrefix = "liquidcast-backup-";

    // Decompression-bomb guards for restore.
    private const long MaxTotalUncompressedBytes = 4L * 1024 * 1024 * 1024; // 4 GB
    private const int MaxEntryCount = 100;

    public sealed record Manifest(int Version, DateTime CreatedAt);

    /// <summary>Builds a backup zip in <paramref name="targetDir"/>; returns its full path.</summary>
    public async Task<string> CreateAsync(string dbPath, string targetDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDir);
        var zipPath = Path.Combine(targetDir, $"{FilePrefix}{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        // Consistent DB snapshot via the SQLite online-backup API (does not lock the live DB).
        var tempDb = Path.Combine(Path.GetTempPath(), $"liquidcast-snap-{Guid.NewGuid():N}.db");
        try
        {
            // Pooling=False so the snapshot file handle is released on dispose (we zip it next).
            await using (var src = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            await using (var dst = new SqliteConnection($"Data Source={tempDb};Pooling=False"))
            {
                await src.OpenAsync(ct);
                await dst.OpenAsync(ct);
                src.BackupDatabase(dst);
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempDb, DbEntry);
                var entry = zip.CreateEntry(ManifestEntry);
                await using var es = entry.Open();
                await JsonSerializer.SerializeAsync(es, new Manifest(1, DateTime.UtcNow), cancellationToken: ct);
            }
        }
        finally
        {
            if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { /* best effort */ }
        }
        return zipPath;
    }

    /// <summary>Deletes the oldest backup zips beyond <paramref name="keep"/>. Returns count removed.</summary>
    public int Prune(string targetDir, int keep)
    {
        if (keep <= 0 || !Directory.Exists(targetDir)) return 0;
        var files = new DirectoryInfo(targetDir)
            .EnumerateFiles($"{FilePrefix}*.zip")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(keep)
            .ToList();
        foreach (var f in files) try { f.Delete(); } catch { /* best effort */ }
        return files.Count;
    }

    /// <summary>
    /// Restores a backup zip: replaces the live DB. Clears SQLite connection pools first so the
    /// file can be overwritten, and drops stale WAL/SHM sidecars.
    /// </summary>
    public async Task RestoreAsync(Stream zipStream, string dbPath, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"liquidcast-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var local = Path.Combine(tempDir, "backup.zip");
            await using (var fs = File.Create(local))
                await zipStream.CopyToAsync(fs, ct);

            using var zip = ZipFile.OpenRead(local);
            if (zip.GetEntry(ManifestEntry) is null || zip.GetEntry(DbEntry) is null)
                throw new InvalidOperationException("Not a valid Liquidcast backup.");
            if (zip.Entries.Count > MaxEntryCount)
                throw new InvalidOperationException("Backup has too many entries.");
            if (zip.Entries.Sum(e => e.Length) > MaxTotalUncompressedBytes)
                throw new InvalidOperationException("Backup is too large to restore.");

            SqliteConnection.ClearAllPools();

            Extract(zip.GetEntry(DbEntry)!, dbPath);
            foreach (var side in new[] { dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(side)) try { File.Delete(side); } catch { /* best effort */ }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    private static void Extract(ZipArchiveEntry entry, string destPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        entry.ExtractToFile(destPath, overwrite: true);
    }
}
