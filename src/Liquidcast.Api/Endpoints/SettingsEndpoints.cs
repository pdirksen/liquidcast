using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Liquidcast.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.Settings.AsNoTracking().FirstAsync(s => s.Id == 1)));

        // Settings are baked into the generated Liquidsoap script, so applying them
        // regenerates the script and restarts the process (brief, admin-initiated).
        g.MapPut("/", async (AppSetting dto, AppDbContext db, RuntimeConfig cfg, LiquidsoapProcess ls) =>
        {
            var s = await db.Settings.FirstAsync(x => x.Id == 1);
            s.IcecastHost = dto.IcecastHost;
            s.IcecastPort = dto.IcecastPort;
            s.IcecastPassword = dto.IcecastPassword;
            s.IcecastMount = dto.IcecastMount;
            s.StreamName = dto.StreamName;
            s.StreamDescription = dto.StreamDescription;
            s.Bitrate = dto.Bitrate;
            s.PublicStreamUrl = string.IsNullOrWhiteSpace(dto.PublicStreamUrl) ? null : dto.PublicStreamUrl.Trim();
            s.IcecastAdminUser = dto.IcecastAdminUser;
            s.IcecastAdminPassword = dto.IcecastAdminPassword;
            s.DefaultCrossfadeSec = dto.DefaultCrossfadeSec;
            s.FadeInSec = dto.FadeInSec;
            s.FadeOutSec = dto.FadeOutSec;
            s.FallbackMode = dto.FallbackMode;
            s.FallbackPlaylistId = dto.FallbackPlaylistId;
            s.LiquidsoapPath = dto.LiquidsoapPath;
            s.ControlMode = dto.ControlMode;
            s.TelnetPort = dto.TelnetPort;
            s.LiquidsoapLogLevel = dto.LiquidsoapLogLevel;
            s.DataPath = dto.DataPath;
            s.MaxUploadSizeMb = dto.MaxUploadSizeMb;
            s.LoginRateLimitPermitLimit = dto.LoginRateLimitPermitLimit;
            s.LoginRateLimitWindowSec = dto.LoginRateLimitWindowSec;
            await db.SaveChangesAsync();

            cfg.Update(s);
            cfg.EnsureDirectories();
            ls.Restart();
            return Results.Ok(s);
        });
    }
}
