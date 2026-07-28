using System;
using AlgoForge.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    // Written by hand (the running app locks the build output dotnet-ef needs).
    // Attributes normally live on the generated Designer partial; hosting them on the
    // class itself is equivalent for applying the migration.
    [DbContext(typeof(AlgoForgeDbContext))]
    [Migration("20260728200000_AddAttendeeInviteSentAt")]
    public partial class AddAttendeeInviteSentAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InviteSentAt",
                table: "Attendees",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InviteSentAt",
                table: "Attendees");
        }
    }
}
