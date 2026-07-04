using Liquidcast.Api.Data;
using Liquidcast.Api.Endpoints;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Services;

/// <summary>
/// Runs the daily scheduled backup while the app is up. Every minute it checks whether the
/// configured run time has passed today and no backup has been taken since; if so it creates one
/// and prunes old zips. (Only fires while the process is running.)
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RuntimeConfig _cfg;
    private readonly ILogger<BackupScheduler> _log;

    public BackupScheduler(IServiceScopeFactory scopes, RuntimeConfig cfg, ILogger<BackupScheduler> log)
    {
        _scopes = scopes;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await TickAsync(stoppingToken); }
                catch (Exception ex) { _log.LogError(ex, "Scheduled backup failed."); }
            }
        }
        catch (OperationCanceledException) { /* host shutting down */ }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var backup = scope.ServiceProvider.GetRequiredService<BackupService>();

        var b = await db.BackupSettings.FirstOrDefaultAsync(ct);
        if (b is null || !b.Enabled || !TimeOnly.TryParse(b.ScheduleTime, out var time)) return;

        var dueToday = DateTime.Now.Date.Add(time.ToTimeSpan());
        if (DateTime.Now < dueToday) return;                              // not yet time today
        if (b.LastBackupAt is { } last && last.ToLocalTime() >= dueToday) return; // already ran

        var dbPath = ((SqliteConnection)db.Database.GetDbConnection()).DataSource;
        var target = BackupEndpoints.EffectiveTarget(_cfg, b.TargetPath);
        await backup.CreateAsync(dbPath, target, ct);
        backup.Prune(target, b.KeepCount);
        b.LastBackupAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Scheduled backup written to {Target}.", target);
    }
}
