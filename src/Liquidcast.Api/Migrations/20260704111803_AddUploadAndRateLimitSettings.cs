using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadAndRateLimitSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LoginRateLimitPermitLimit",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoginRateLimitWindowSec",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxUploadSizeMb",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "LoginRateLimitPermitLimit", "LoginRateLimitWindowSec", "MaxUploadSizeMb" },
                values: new object[] { 5, 60, 200 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginRateLimitPermitLimit",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LoginRateLimitWindowSec",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaxUploadSizeMb",
                table: "Settings");
        }
    }
}
