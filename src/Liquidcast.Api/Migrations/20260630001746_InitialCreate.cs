using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CrossfadeOverrideSec = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IcecastHost = table.Column<string>(type: "TEXT", nullable: false),
                    IcecastPort = table.Column<int>(type: "INTEGER", nullable: false),
                    IcecastPassword = table.Column<string>(type: "TEXT", nullable: false),
                    IcecastMount = table.Column<string>(type: "TEXT", nullable: false),
                    StreamName = table.Column<string>(type: "TEXT", nullable: false),
                    StreamDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Bitrate = table.Column<int>(type: "INTEGER", nullable: false),
                    IcecastAdminUser = table.Column<string>(type: "TEXT", nullable: false),
                    IcecastAdminPassword = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultCrossfadeSec = table.Column<double>(type: "REAL", nullable: false),
                    FadeInSec = table.Column<double>(type: "REAL", nullable: false),
                    FadeOutSec = table.Column<double>(type: "REAL", nullable: false),
                    FallbackMode = table.Column<int>(type: "INTEGER", nullable: false),
                    FallbackPlaylistId = table.Column<int>(type: "INTEGER", nullable: true),
                    LiquidsoapPath = table.Column<string>(type: "TEXT", nullable: true),
                    ControlMode = table.Column<int>(type: "INTEGER", nullable: false),
                    TelnetPort = table.Column<int>(type: "INTEGER", nullable: false),
                    DataPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    StoredPath = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    Album = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSec = table.Column<double>(type: "REAL", nullable: false),
                    Bitrate = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Recurrence = table.Column<int>(type: "INTEGER", nullable: false),
                    HardCut = table.Column<bool>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CueInSec = table.Column<double>(type: "REAL", nullable: true),
                    CueOutSec = table.Column<double>(type: "REAL", nullable: true),
                    CrossfadeSec = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "Bitrate", "ControlMode", "DataPath", "DefaultCrossfadeSec", "FadeInSec", "FadeOutSec", "FallbackMode", "FallbackPlaylistId", "IcecastAdminPassword", "IcecastAdminUser", "IcecastHost", "IcecastMount", "IcecastPassword", "IcecastPort", "LiquidsoapPath", "StreamDescription", "StreamName", "TelnetPort" },
                values: new object[] { 1, 128, 1, "data", 3.0, 2.0, 3.0, 0, null, "hackme", "admin", "localhost", "/stream", "hackme", 8000, null, "Powered by Liquidcast", "Liquidcast", 1234 });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Username",
                table: "AdminUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId",
                table: "PlaylistItems",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_TrackId",
                table: "PlaylistItems",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_PlaylistId",
                table: "ScheduleSlots",
                column: "PlaylistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminUsers");

            migrationBuilder.DropTable(
                name: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "ScheduleSlots");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "Playlists");
        }
    }
}
