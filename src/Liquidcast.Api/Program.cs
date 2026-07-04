using System.Text;
using System.Text.Json.Serialization;
using Liquidcast.Api.Data;
using Liquidcast.Api.Endpoints;
using Liquidcast.Api.Hubs;
using Liquidcast.Api.Models;
using Liquidcast.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Data directory + SQLite path.
var dataPath = Path.GetFullPath(builder.Configuration["DataPath"] ?? "data");
Directory.CreateDirectory(dataPath);
var dbPath = Path.Combine(dataPath, "liquidcast.db");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// Core singletons.
builder.Services.AddSingleton<RuntimeConfig>();
builder.Services.AddSingleton<StreamState>();
builder.Services.AddSingleton<ScriptGenerator>();
builder.Services.AddSingleton<LiquidsoapClient>();
builder.Services.AddSingleton<IcecastClient>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<TrackService>();
builder.Services.AddHttpClient();

// Persistent Liquidsoap process + background workers.
builder.Services.AddSingleton<LiquidsoapProcess>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiquidsoapProcess>());
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService<MonitorService>();

// Backups.
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();

builder.Services.AddSignalR();

// EF navigation back-references create cycles; skip them when serializing.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// AuthN: JWT, read from cookie or Authorization header (so SignalR ws works too).
var jwt = new JwtTokenService(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = JwtTokenService.Issuer,
            ValidAudience = JwtTokenService.Audience,
            IssuerSigningKey = jwt.Key,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) &&
                    ctx.Request.Cookies.TryGetValue(AuthEndpoints.CookieName, out var c))
                    ctx.Token = c;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Dev CORS for the Vite dev server.
const string DevCors = "dev";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// --- startup: migrate, seed, load runtime config ---
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var settings = db.Settings.First(s => s.Id == 1);
    settings.DataPath = dataPath;

    // Deployment-time overrides (containers): connection settings come from the
    // environment so the app can reach Icecast by its compose service name.
    var cfgRoot = builder.Configuration;
    void Env(string key, Action<string> set)
    {
        var v = cfgRoot[key];
        if (!string.IsNullOrWhiteSpace(v)) set(v);
    }
    Env("ICECAST_HOST", v => settings.IcecastHost = v);
    Env("ICECAST_PORT", v => { if (int.TryParse(v, out var p)) settings.IcecastPort = p; });
    Env("ICECAST_MOUNT", v => settings.IcecastMount = v);
    Env("ICECAST_SOURCE_PASSWORD", v => settings.IcecastPassword = v);
    Env("ICECAST_ADMIN_USER", v => settings.IcecastAdminUser = v);
    Env("ICECAST_ADMIN_PASSWORD", v => settings.IcecastAdminPassword = v);
    db.SaveChanges();

    var rc = sp.GetRequiredService<RuntimeConfig>();
    rc.Update(settings);
    rc.EnsureDirectories();

    var startupLog = sp.GetRequiredService<ILogger<Program>>();
    startupLog.LogInformation(
        "Liquidcast starting — data={Data}, db={Db}, control={Control}, telnetPort={Port}, " +
        "icecast={Host}:{IcePort}{Mount}, bitrate={Bitrate}, liquidsoap={Ls}",
        rc.DataPathAbsolute, dbPath, settings.EffectiveControlMode, settings.TelnetPort,
        settings.IcecastHost, settings.IcecastPort, settings.IcecastMount, settings.Bitrate,
        settings.LiquidsoapPath ?? Environment.GetEnvironmentVariable("LIQUIDSOAP_PATH") ?? "(PATH)");

    // Seed the single admin user from env (defaults admin/admin).
    if (!db.AdminUsers.Any())
    {
        var user = builder.Configuration["ADMIN_USER"] ?? "admin";
        var pass = builder.Configuration["ADMIN_PASSWORD"] ?? "admin";
        db.AdminUsers.Add(new AdminUser { Username = user, PasswordHash = PasswordHasher.Hash(pass) });
        db.SaveChanges();
    }

    // Reconcile the tracks table with the tracks dir so MP3s dropped into subfolders are ingested.
    try
    {
        var sync = await sp.GetRequiredService<TrackService>().SyncFromDiskAsync(CancellationToken.None);
        if (sync.Added > 0 || sync.Updated > 0)
            startupLog.LogInformation("Track scan: {Added} added, {Updated} updated", sync.Added, sync.Updated);
    }
    catch (Exception ex) { startupLog.LogWarning(ex, "Track scan on startup failed"); }
}

if (app.Environment.IsDevelopment())
    app.UseCors(DevCors);

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuth();
app.MapTracks();
app.MapPlaylists();
app.MapSchedule();
app.MapSettings();
app.MapStream();
app.MapBackup();
app.MapHub<MonitorHub>("/hubs/monitor");

// SPA fallback: anything not under /api or /hubs serves index.html.
app.MapFallbackToFile("index.html");

app.Run();
