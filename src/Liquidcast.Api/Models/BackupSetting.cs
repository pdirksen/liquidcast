namespace Liquidcast.Api.Models;

/// <summary>Singleton backup configuration row (Id == 1).</summary>
public class BackupSetting
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    /// <summary>Target folder for backup zips. Null/empty → the data path's <c>backups/</c> folder.</summary>
    public string? TargetPath { get; set; }
    /// <summary>Daily run time as <c>HH:mm</c>.</summary>
    public string ScheduleTime { get; set; } = "02:00";
    /// <summary>How many backup zips to keep; older ones are pruned.</summary>
    public int KeepCount { get; set; } = 10;
    public DateTime? LastBackupAt { get; set; }
}
