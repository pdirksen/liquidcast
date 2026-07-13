using Liquidcast.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Liquidcast.Api.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<ScheduledTrack> ScheduledTracks => Set<ScheduledTrack>();
    public DbSet<ScheduleLine> ScheduleLines => Set<ScheduleLine>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<BackupSetting> BackupSettings => Set<BackupSetting>();
    public DbSet<ListenerSample> ListenerSamples => Set<ListenerSample>();
    public DbSet<PlayHistory> PlayHistory => Set<PlayHistory>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PlaylistItem>()
            .HasOne(pi => pi.Playlist)
            .WithMany(p => p.Items)
            .HasForeignKey(pi => pi.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<PlaylistItem>()
            .HasOne(pi => pi.Track)
            .WithMany(t => t.PlaylistItems)
            .HasForeignKey(pi => pi.TrackId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ScheduledTrack>()
            .HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScheduledTrack>().HasIndex(s => s.StartUtc);

        // Id IS the line number (0..4), assigned by the client — never an identity column.
        b.Entity<ScheduleLine>().Property(l => l.Id).ValueGeneratedNever();

        b.Entity<AdminUser>().HasIndex(u => u.Username).IsUnique();
        b.Entity<ListenerSample>().HasIndex(s => s.SampleUtc);

        b.Entity<PlayHistory>()
            .HasOne(p => p.Track)
            .WithMany()
            .HasForeignKey(p => p.TrackId)
            .OnDelete(DeleteBehavior.SetNull);
        b.Entity<PlayHistory>().HasIndex(p => p.StartedUtc);

        // Singleton settings rows.
        b.Entity<AppSetting>().HasData(new AppSetting { Id = 1 });
        b.Entity<BackupSetting>().HasData(new BackupSetting { Id = 1 });

        // SQLite drops DateTimeKind on round-trip; force UTC on read so timestamps
        // serialize with a trailing 'Z' and the SPA converts them to local correctly.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                if (prop.ClrType == typeof(DateTime))
                    prop.SetValueConverter(utc);
    }
}
