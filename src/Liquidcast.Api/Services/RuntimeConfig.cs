using Liquidcast.Api.Models;

namespace Liquidcast.Api.Services;

/// <summary>
/// Process-wide resolved configuration: the current settings snapshot plus the
/// absolute filesystem paths derived from it. Refreshed from the DB at startup
/// and whenever settings change.
/// </summary>
public class RuntimeConfig
{
    private readonly object _gate = new();
    private AppSetting _settings = new();

    public AppSetting Settings
    {
        get { lock (_gate) return _settings; }
    }

    public void Update(AppSetting settings)
    {
        lock (_gate) _settings = settings;
    }

    /// <summary>Absolute data directory (tracks, fallback, db, generated script, socket).</summary>
    public string DataPathAbsolute => Path.GetFullPath(Settings.DataPath);
    public string TracksDir => Path.Combine(DataPathAbsolute, "tracks");
    public string FallbackDir => Path.Combine(DataPathAbsolute, "fallback");
    public string ScriptPath => Path.Combine(DataPathAbsolute, "liquidcast.liq");
    public string SocketPath => Path.Combine(DataPathAbsolute, "liquidcast.sock");
    public string LogPath => Path.Combine(DataPathAbsolute, "liquidsoap.log");
    public long MaxUploadBytes => (long)Settings.MaxUploadSizeMb * 1024 * 1024;

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataPathAbsolute);
        Directory.CreateDirectory(TracksDir);
        Directory.CreateDirectory(FallbackDir);
    }
}
