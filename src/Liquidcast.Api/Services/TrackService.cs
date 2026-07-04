using System.Security.Cryptography;
using Liquidcast.Api.Data;
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
        await using (var fs = File.Create(tmp))
        {
            await content.CopyToAsync(fs, ct);
            size = fs.Length;
        }
        await using (var read = File.OpenRead(tmp))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct)).ToLowerInvariant();
        }

        var existing = await _db.Tracks.FirstOrDefaultAsync(t => t.Sha256 == hash, ct);
        if (existing is not null)
        {
            File.Delete(tmp);
            return new UploadResult(existing, true);
        }

        var safeName = MakeUniqueName(originalName, targetDir);
        var finalPath = Path.Combine(targetDir, safeName);
        File.Move(tmp, finalPath, overwrite: false);

        var meta = ReadMetadata(finalPath);
        if (meta.DurationSec <= 0)
        {
            // ATL couldn't decode any audio frames — not a real MP3 despite the extension.
            File.Delete(finalPath);
            throw new InvalidOperationException("File is not a valid MP3.");
        }

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

    /// <summary>Recursively scans TracksDir and reconciles the DB: fixes RelativePath on known
    /// files and ingests MP3s found in subfolders (e.g. dropped in manually). Only unknown files
    /// are hashed/parsed, so re-scanning an existing library is cheap.</summary>
    public async Task<SyncResult> SyncFromDiskAsync(CancellationToken ct)
    {
        _cfg.EnsureDirectories();
        int added = 0, updated = 0;
        var known = await _db.Tracks.ToDictionaryAsync(t => t.StoredPath, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var file in Directory.EnumerateFiles(_cfg.TracksDir, "*.mp3", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue; // skip .upload-*.tmp and other dotfiles

            var rel = RelPath(file);
            if (known.TryGetValue(file, out var existing))
            {
                if (existing.RelativePath != rel || existing.FileName != name)
                {
                    existing.RelativePath = rel;
                    existing.FileName = name;
                    updated++;
                }
                continue;
            }

            string hash;
            await using (var read = File.OpenRead(file))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct)).ToLowerInvariant();

            var byHash = await _db.Tracks.FirstOrDefaultAsync(t => t.Sha256 == hash, ct);
            if (byHash is not null)
            {
                // Same content already known. If its recorded file is gone, this is a move —
                // realign the row to the new location. If the original still exists, this is a
                // genuine on-disk duplicate → ignore it (hash is identity, as upload dedup does).
                if (!File.Exists(byHash.StoredPath))
                {
                    byHash.StoredPath = file;
                    byHash.RelativePath = rel;
                    byHash.FileName = name;
                    updated++;
                }
                continue;
            }

            var meta = ReadMetadata(file);
            _db.Tracks.Add(new Track
            {
                FileName = name,
                StoredPath = file,
                RelativePath = rel,
                Title = meta.Title,
                Artist = meta.Artist,
                Album = meta.Album,
                DurationSec = meta.DurationSec,
                Bitrate = meta.Bitrate,
                SizeBytes = new FileInfo(file).Length,
                Sha256 = hash,
                UploadedAt = DateTime.UtcNow,
            });
            added++;
        }

        if (added > 0 || updated > 0) await _db.SaveChangesAsync(ct);
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

        _db.Tracks.Remove(track);
        await _db.SaveChangesAsync(ct);
        try { if (File.Exists(track.StoredPath)) File.Delete(track.StoredPath); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not delete file {Path}", track.StoredPath); }
        return true;
    }

    private record Meta(string? Title, string? Artist, string? Album, double DurationSec, int Bitrate);

    private static Meta ReadMetadata(string path)
    {
        try
        {
            var t = new ATL.Track(path);
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
