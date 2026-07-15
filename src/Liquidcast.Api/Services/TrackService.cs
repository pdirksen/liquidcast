using System.Security.Cryptography;
using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Services;

public class TrackService
{
    private readonly AppDbContext _db;
    private readonly RuntimeConfig _cfg;
    private readonly ILogger<TrackService> _log;

    public TrackService(AppDbContext db, RuntimeConfig cfg, ILogger<TrackService> log)
    {
        _db = db;
        _cfg = cfg;
        _log = log;
    }

    public record UploadResult(Track Track, bool AlreadyExisted);

    /// <summary>Stores an uploaded MP3 in the data path (optionally under a subfolder) and records its metadata.</summary>
    public async Task<UploadResult> SaveUploadAsync(Stream content, string originalName, string? folder, CancellationToken ct)
    {
        if (!originalName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .mp3 files are supported.");

        _cfg.EnsureDirectories();
        var targetDir = ResolveFolderDir(folder);
        Directory.CreateDirectory(targetDir);
        var tmp = Path.Combine(_cfg.TracksDir, $".upload-{Guid.NewGuid():N}.tmp");

        string hash;
        long size;
        Meta meta;
        await using (var fs = File.Create(tmp))
        {
            await content.CopyToAsync(fs, ct);
            size = fs.Length;
        }
        await using (var read = File.OpenRead(tmp))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct)).ToLowerInvariant();
            // Reuse the same handle for tag/duration parsing — one read instead of two.
            meta = ReadMetadata(read);
        }

        var existing = await _db.Tracks.FirstOrDefaultAsync(t => t.Sha256 == hash, ct);
        if (existing is not null)
        {
            File.Delete(tmp);
            return new UploadResult(existing, true);
        }

        if (meta.DurationSec <= 0)
        {
            // ATL couldn't decode any audio frames — not a real MP3 despite the extension.
            File.Delete(tmp);
            throw new InvalidOperationException("File is not a valid MP3.");
        }

        var safeName = MakeUniqueName(originalName, targetDir);
        var finalPath = Path.Combine(targetDir, safeName);
        File.Move(tmp, finalPath, overwrite: false);

        var track = new Track
        {
            FileName = safeName,
            StoredPath = finalPath,
            RelativePath = RelPath(finalPath),
            Title = meta.Title,
            Artist = meta.Artist,
            Album = meta.Album,
            DurationSec = meta.DurationSec,
            Bitrate = meta.Bitrate,
            SizeBytes = size,
            Sha256 = hash,
            UploadedAt = DateTime.UtcNow,
        };
        _db.Tracks.Add(track);
        await _db.SaveChangesAsync(ct);
        return new UploadResult(track, false);
    }

    public record SyncResult(int Added, int Updated);

    /// <summary>Wipes all track rows plus anything referencing them (playlist items, scheduled
    /// tracks) so a subsequent <see cref="SyncFromDiskAsync"/> rebuilds the library from scratch.
    /// Audio files on disk are left untouched.</summary>
    public async Task ClearAllAsync(CancellationToken ct)
    {
        await _db.PlaylistItems.ExecuteDeleteAsync(ct);
        await _db.ScheduledTracks.ExecuteDeleteAsync(ct);
        await _db.Tracks.ExecuteDeleteAsync(ct);
    }

    /// <summary>Recursively scans TracksDir and reconciles the DB: fixes RelativePath on known
    /// files and ingests MP3s found in subfolders (e.g. dropped in manually). Only unknown files
    /// are hashed/parsed, so re-scanning an existing library is cheap. <paramref name="progress"/>
    /// (scanned, added, updated) is invoked periodically during the walk.</summary>
    public async Task<SyncResult> SyncFromDiskAsync(CancellationToken ct, Action<int, int, int>? progress = null)
    {
        const int saveBatchSize = 200;   // keep SQLite write transactions short during big imports
        const int progressEvery = 50;

        _cfg.EnsureDirectories();
        int scanned = 0, added = 0, updated = 0, unsaved = 0;
        var known = await _db.Tracks.ToDictionaryAsync(t => t.StoredPath, StringComparer.OrdinalIgnoreCase, ct);

        // Hash → track map so dedup/move detection is a memory lookup instead of a DB query per
        // file; also makes tracks added earlier in this same scan visible to later duplicates.
        var byHash = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in known.Values)
            if (t.Sha256 is not null) byHash.TryAdd(t.Sha256, t);

        foreach (var file in Directory.EnumerateFiles(_cfg.TracksDir, "*.mp3", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue; // skip .upload-*.tmp and other dotfiles

            if (++scanned % progressEvery == 0) progress?.Invoke(scanned, added, updated);

            var rel = RelPath(file);
            if (known.TryGetValue(file, out var existing))
            {
                if (existing.RelativePath != rel || existing.FileName != name)
                {
                    existing.RelativePath = rel;
                    existing.FileName = name;
                    updated++;
                    unsaved++;
                }
                continue;
            }

            string hash;
            long size;
            await using var fs = File.OpenRead(file);
            hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            size = fs.Length;

            if (byHash.TryGetValue(hash, out var sameContent))
            {
                // Same content already known. If its recorded file is gone, this is a move —
                // realign the row to the new location. If the original still exists, this is a
                // genuine on-disk duplicate → ignore it (hash is identity, as upload dedup does).
                if (!File.Exists(sameContent.StoredPath))
                {
                    sameContent.StoredPath = file;
                    sameContent.RelativePath = rel;
                    sameContent.FileName = name;
                    updated++;
                    unsaved++;
                }
                continue;
            }

            // Reuse the already-open handle for tag/duration parsing — one file read instead of two.
            var meta = ReadMetadata(fs);
            var track = new Track
            {
                FileName = name,
                StoredPath = file,
                RelativePath = rel,
                Title = meta.Title,
                Artist = meta.Artist,
                Album = meta.Album,
                DurationSec = meta.DurationSec,
                Bitrate = meta.Bitrate,
                SizeBytes = size,
                Sha256 = hash,
                UploadedAt = DateTime.UtcNow,
            };
            _db.Tracks.Add(track);
            byHash.TryAdd(hash, track);
            added++;

            if (++unsaved >= saveBatchSize)
            {
                await _db.SaveChangesAsync(ct);
                unsaved = 0;
            }
        }

        if (unsaved > 0) await _db.SaveChangesAsync(ct);
        progress?.Invoke(scanned, added, updated);
        return new SyncResult(added, updated);
    }

    /// <summary>Path relative to TracksDir, forward-slashed (incl. filename).</summary>
    private string RelPath(string absolutePath) =>
        Path.GetRelativePath(_cfg.TracksDir, absolutePath).Replace('\\', '/');

    // --- folder operations (directories under TracksDir) ---

    /// <summary>All subdirectories under TracksDir, as forward-slashed relative paths (recursive).</summary>
    public IEnumerable<string> ListFolders()
    {
        if (!Directory.Exists(_cfg.TracksDir)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(_cfg.TracksDir, "*", SearchOption.AllDirectories)
            .Select(RelPath)
            .Where(r => !r.Split('/').Any(seg => seg.StartsWith('.'))) // skip hidden dirs
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Creates a (possibly nested) folder under TracksDir. Returns its relative path.</summary>
    public string CreateFolder(string? path)
    {
        var dir = ResolveFolderDir(path);
        if (string.Equals(dir, _cfg.TracksDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A folder name is required.");
        Directory.CreateDirectory(dir);
        return RelPath(dir);
    }

    /// <summary>Deletes a folder under TracksDir, but only when it is completely empty.</summary>
    public void DeleteFolder(string? path)
    {
        var dir = ResolveFolderDir(path);
        if (string.Equals(dir, _cfg.TracksDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete the root folder.");
        if (!Directory.Exists(dir)) return; // already gone — treat as success
        if (Directory.EnumerateFileSystemEntries(dir).Any())
            throw new InvalidOperationException("Folder is not empty.");
        Directory.Delete(dir);
    }

    /// <summary>Moves a track's file into <paramref name="folder"/> (under TracksDir) and updates its paths.</summary>
    public async Task<Track> MoveTrackAsync(int id, string? folder, CancellationToken ct)
    {
        var track = await _db.Tracks.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException("Track not found.");

        var targetDir = ResolveFolderDir(folder);
        var currentDir = Path.GetDirectoryName(track.StoredPath)!;
        if (string.Equals(Path.GetFullPath(targetDir), Path.GetFullPath(currentDir), StringComparison.OrdinalIgnoreCase))
            return track; // already in that folder — no-op

        Directory.CreateDirectory(targetDir);
        var name = MakeUniqueName(track.FileName, targetDir);
        var dest = Path.Combine(targetDir, name);
        File.Move(track.StoredPath, dest, overwrite: false);

        track.StoredPath = dest;
        track.FileName = name;
        track.RelativePath = RelPath(dest);
        await _db.SaveChangesAsync(ct);
        return track;
    }

    /// <summary>Resolves a user-supplied subfolder to an absolute dir under TracksDir, sanitizing
    /// each segment and rejecting traversal (., .., separators, invalid chars).</summary>
    private string ResolveFolderDir(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return _cfg.TracksDir;
        var invalid = Path.GetInvalidFileNameChars();
        var segs = folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(seg => { foreach (var c in invalid) seg = seg.Replace(c, '_'); return seg; })
            .Where(seg => seg is not ("." or ".." or ""))
            .ToArray();
        return segs.Length == 0 ? _cfg.TracksDir : Path.Combine(new[] { _cfg.TracksDir }.Concat(segs).ToArray());
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var track = await _db.Tracks.FindAsync(new object[] { id }, ct);
        if (track is null) return false;

        bool referenced = await _db.PlaylistItems.AnyAsync(pi => pi.TrackId == id, ct);
        if (referenced)
            throw new InvalidOperationException("Track is used in one or more playlists.");

        bool scheduled = await _db.ScheduledTracks.AnyAsync(s => s.TrackId == id, ct);
        if (scheduled)
            throw new InvalidOperationException("Track is scheduled.");

        _db.Tracks.Remove(track);
        await _db.SaveChangesAsync(ct);
        try { if (File.Exists(track.StoredPath)) File.Delete(track.StoredPath); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not delete file {Path}", track.StoredPath); }
        return true;
    }

    private record Meta(string? Title, string? Artist, string? Album, double DurationSec, int Bitrate);

    /// <summary>Reads tag/duration metadata from an already-open, seekable stream (positioned
    /// wherever the caller left it — this seeks to 0 itself) so hashing and metadata parsing
    /// share a single file read instead of two.</summary>
    private static Meta ReadMetadata(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var t = new ATL.Track(stream, ".mp3");
            var title = Sanitize(t.Title);
            var artist = Sanitize(t.Artist);
            var album = Sanitize(t.Album);
            return new Meta(title, artist, album, t.Duration, t.Bitrate);
        }
        catch
        {
            return new Meta(null, null, null, 0, 0);
        }
    }

    // ID3 tags are attacker-controlled (an uploaded file's metadata). Strip control
    // characters — in particular \r/\n, which would otherwise reach the Liquidsoap
    // control socket's line-based protocol and let a crafted tag inject extra commands.
    private static string? Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string MakeUniqueName(string original, string targetDir)
    {
        var name = Path.GetFileNameWithoutExtension(original);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        var candidate = name + ".mp3";
        int i = 1;
        while (File.Exists(Path.Combine(targetDir, candidate)))
            candidate = $"{name}-{i++}.mp3";
        return candidate;
    }
}
