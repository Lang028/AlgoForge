using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegateInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegateInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revoked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegateInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegateInvites_AspNetUsers_ClaimedByUserId",
                        column: x => x.ClaimedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegateInvites_Attendees_AttendeeId",
                        column: x => x.AttendeeId,
                        principalTable: "Attendees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DelegateInvites_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegateInvites_AttendeeId",
                table: "DelegateInvites",
                column: "AttendeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegateInvites_ClaimedByUserId",
                table: "DelegateInvites",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegateInvites_EventId",
                table: "DelegateInvites",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegateInvites");
        }
    }
}
