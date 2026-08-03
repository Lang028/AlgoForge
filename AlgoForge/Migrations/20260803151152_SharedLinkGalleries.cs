using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class SharedLinkGalleries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GalleryMode",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "Events",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ShareToken",
                table: "Events",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_ShareToken",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "GalleryMode",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "Events");
        }
    }
}
