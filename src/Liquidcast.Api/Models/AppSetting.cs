using System.ComponentModel.DataAnnotations.Schema;

namespace Liquidcast.Api.Models;

public enum FallbackMode
{
    Silence = 0,
    Playlist = 1
}

public enum ControlMode
{
    Tcp = 0,
    UnixSocket = 1
}

/// <summary>Singleton configuration row (Id == 1).</summary>
public class AppSetting
{
    public int Id { get; set; } = 1;

    // Icecast target
    public string IcecastHost { get; set; } = "localhost";
    public int IcecastPort { get; set; } = 8000;
    public string IcecastPassword { get; set; } = "hackme";
    public string IcecastMount { get; set; } = "/stream";
    public string StreamName { get; set; } = "Liquidcast";
    public string StreamDescription { get; set; } = "Powered by Liquidcast";
    public int Bitrate { get; set; } = 128;

    // Icecast admin (for listener stats)
    public string IcecastAdminUser { get; set; } = "admin";
    public string IcecastAdminPassword { get; set; } = "hackme";

    // Crossfade defaults (seconds)
    public double DefaultCrossfadeSec { get; set; } = 3.0;
    public double FadeInSec { get; set; } = 2.0;
    public double FadeOutSec { get; set; } = 3.0;

    // Fallback
    public FallbackMode FallbackMode { get; set; } = FallbackMode.Silence;
    public int? FallbackPlaylistId { get; set; }

    // Liquidsoap process / control
    public string? LiquidsoapPath { get; set; } // null => resolve from PATH
    /// <summary>Preferred control transport. Unix sockets are unavailable on Windows; the
    /// runtime falls back to TCP there regardless of this value (see EffectiveControlMode).</summary>
    public ControlMode ControlMode { get; set; } = ControlMode.UnixSocket;
    public int TelnetPort { get; set; } = 1234;
    /// <summary>Liquidsoap log verbosity: 1=critical … 3=info (default) … 4=debug.</summary>
    public int LiquidsoapLogLevel { get; set; } = 3;

    /// <summary>Control mode actually usable on the current OS.</summary>
    [NotMapped]
    public ControlMode EffectiveControlMode =>
        OperatingSystem.IsWindows() ? ControlMode.Tcp : ControlMode;

    // Storage
    public string DataPath { get; set; } = "data";

    // Uploads / login security
    public int MaxUploadSizeMb { get; set; } = 200;
    public int LoginRateLimitPermitLimit { get; set; } = 5;
    public int LoginRateLimitWindowSec { get; set; } = 60;
}
