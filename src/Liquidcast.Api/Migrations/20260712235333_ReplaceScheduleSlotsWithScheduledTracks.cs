using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceScheduleSlotsWithScheduledTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleSlots");

            migrationBuilder.CreateTable(
                name: "ScheduledTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Line = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Override = table.Column<bool>(type: "INTEGER", nullable: false),
                    CueInSec = table.Column<double>(type: "REAL", nullable: true),
                    CueOutSec = table.Column<double>(type: "REAL", nullable: true),
                    CrossfadeSec = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTracks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTracks_StartUtc",
                table: "ScheduledTracks",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTracks_TrackId",
                table: "ScheduledTracks",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTracks");

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HardCut = table.Column<bool>(type: "INTEGER", nullable: false),
                    Loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    Recurrence = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_PlaylistId",
                table: "ScheduleSlots",
                column: "PlaylistId");
        }
    }
}
