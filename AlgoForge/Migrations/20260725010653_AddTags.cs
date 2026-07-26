using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class AddTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Albums_Events_eventId",
                table: "Albums");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                table: "FaceClusters");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                table: "FaceClusters");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceDetections_FaceClusters_FaceClusterId",
                table: "FaceDetections");

            migrationBuilder.DropForeignKey(
                name: "FK_Photos_Albums_AlbumId",
                table: "Photos");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Albums",
                table: "Albums");

            migrationBuilder.RenameTable(
                name: "Albums",
                newName: "Album");

            migrationBuilder.RenameIndex(
                name: "IX_Albums_eventId",
                table: "Album",
                newName: "IX_Album_eventId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Album",
                table: "Album",
                column: "AlbumId");

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaceDetectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaggedAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tags_Attendees_TaggedAttendeeId",
                        column: x => x.TaggedAttendeeId,
                        principalTable: "Attendees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tags_FaceDetections_FaceDetectionId",
                        column: x => x.FaceDetectionId,
                        principalTable: "FaceDetections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_CreatedByUserId",
                table: "Tags",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_FaceDetectionId",
                table: "Tags",
                column: "FaceDetectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_TaggedAttendeeId",
                table: "Tags",
                column: "TaggedAttendeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Album_Events_eventId",
                table: "Album",
                column: "eventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                table: "FaceClusters",
                column: "IdentifiedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                table: "FaceClusters",
                column: "LinkedAttendeeId",
                principalTable: "Attendees",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FaceDetections_FaceClusters_FaceClusterId",
                table: "FaceDetections",
                column: "FaceClusterId",
                principalTable: "FaceClusters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Photos_Album_AlbumId",
                table: "Photos",
                column: "AlbumId",
                principalTable: "Album",
                principalColumn: "AlbumId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Album_Events_eventId",
                table: "Album");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                table: "FaceClusters");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                table: "FaceClusters");

            migrationBuilder.DropForeignKey(
                name: "FK_FaceDetections_FaceClusters_FaceClusterId",
                table: "FaceDetections");

            migrationBuilder.DropForeignKey(
                name: "FK_Photos_Album_AlbumId",
                table: "Photos");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Album",
                table: "Album");

            migrationBuilder.RenameTable(
                name: "Album",
                newName: "Albums");

            migrationBuilder.RenameIndex(
                name: "IX_Album_eventId",
                table: "Albums",
                newName: "IX_Albums_eventId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Albums",
                table: "Albums",
                column: "AlbumId");

            migrationBuilder.AddForeignKey(
                name: "FK_Albums_Events_eventId",
                table: "Albums",
                column: "eventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                table: "FaceClusters",
                column: "IdentifiedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                table: "FaceClusters",
                column: "LinkedAttendeeId",
                principalTable: "Attendees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FaceDetections_FaceClusters_FaceClusterId",
                table: "FaceDetections",
                column: "FaceClusterId",
                principalTable: "FaceClusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Photos_Albums_AlbumId",
                table: "Photos",
                column: "AlbumId",
                principalTable: "Albums",
                principalColumn: "AlbumId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
