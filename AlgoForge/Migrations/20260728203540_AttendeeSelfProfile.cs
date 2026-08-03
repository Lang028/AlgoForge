using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class AttendeeSelfProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "About",
                table: "Attendees",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "About",
                table: "Attendees");
        }
    }
}
