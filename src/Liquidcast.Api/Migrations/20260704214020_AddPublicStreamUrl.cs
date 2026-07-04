using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicStreamUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicStreamUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PublicStreamUrl",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicStreamUrl",
                table: "Settings");
        }
    }
}
