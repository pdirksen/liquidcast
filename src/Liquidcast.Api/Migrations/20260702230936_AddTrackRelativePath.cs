using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Liquidcast.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackRelativePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelativePath",
                table: "Tracks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Existing tracks are all in the root of the tracks dir → relative path == filename.
            migrationBuilder.Sql("UPDATE Tracks SET RelativePath = FileName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelativePath",
                table: "Tracks");
        }
    }
}
