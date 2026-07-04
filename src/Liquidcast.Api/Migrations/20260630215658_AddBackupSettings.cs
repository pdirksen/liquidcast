using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleTime = table.Column<string>(type: "TEXT", nullable: false),
                    KeepCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastBackupAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BackupSettings",
                columns: new[] { "Id", "Enabled", "KeepCount", "LastBackupAt", "ScheduleTime", "TargetPath" },
                values: new object[] { 1, false, 10, null, "02:00", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupSettings");
        }
    }
}
