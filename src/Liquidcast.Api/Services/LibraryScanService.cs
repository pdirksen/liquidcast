namespace Liquidcast.Api.Services;

/// <summary>Runs track-library disk scans off the request/startup path. The initial scan happens
/// in the background after the host starts (boot no longer waits on a large library), and the
/// rescan/clear endpoints trigger scans through <see cref="StartScan"/>. A semaphore serializes
/// scans so the startup scan, rescan and clear+rescan never run concurrently.</summary>
public class LibraryScanService : BackgroundService
{
    public record ScanStatus(bool Scanning, int Scanned, int Added, int Updated);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryScanService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ScanStatus _status = new(false, 0, 0, 0);

    public LibraryScanService(IServiceScopeFactory scopeFactory, ILogger<LibraryScanService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public ScanStatus Status => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reconcile the tracks table with the tracks dir so MP3s dropped into subfolders are
        // ingested. Yield first so host startup completes before the scan begins.
        await Task.Yield();
        try
        {
            var r = await RunScanAsync(clearFirst: false, stoppingToken);
            if (r.Added > 0 || r.Updated > 0)
                _log.LogInformation("Track scan: {Added} added, {Updated} updated", r.Added, r.Updated);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Track scan on startup failed"); }
    }

    /// <summary>Fire-and-forget scan for the rescan/clear endpoints; progress via <see cref="Status"/>.</summary>
    public void StartScan(bool clearFirst)
    {
        _ = Task.Run(async () =>
        {
            try { await RunScanAsync(clearFirst, CancellationToken.None); }
            catch (Exception ex) { _log.LogError(ex, "Track scan failed (clearFirst={Clear})", clearFirst); }
        });
    }

    public async Task<TrackService.SyncResult> RunScanAsync(bool clearFirst, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        _status = new ScanStatus(true, 0, 0, 0);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TrackService>();
            if (clearFirst) await svc.ClearAllAsync(ct);
            var r = await svc.SyncFromDiskAsync(ct, (scanned, added, updated) =>
                _status = new ScanStatus(true, scanned, added, updated));
            _status = new ScanStatus(false, _status.Scanned, r.Added, r.Updated);
            return r;
        }
        catch
        {
            _status = _status with { Scanning = false };
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
