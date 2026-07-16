using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class AddFacePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FaceProcessingStatus",
                table: "Photos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FaceClusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RepresentativeEmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LinkedAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdentifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                        column: x => x.IdentifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                        column: x => x.LinkedAttendeeId,
                        principalTable: "Attendees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaceClusters_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceDetections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaceClusterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoxX = table.Column<double>(type: "float", nullable: false),
                    BoxY = table.Column<double>(type: "float", nullable: false),
                    BoxWidth = table.Column<double>(type: "float", nullable: false),
                    BoxHeight = table.Column<double>(type: "float", nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    EmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceDetections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceDetections_FaceClusters_FaceClusterId",
                        column: x => x.FaceClusterId,
                        principalTable: "FaceClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaceDetections_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusters_EventId",
                table: "FaceClusters",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusters_IdentifiedByUserId",
                table: "FaceClusters",
                column: "IdentifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusters_LinkedAttendeeId",
                table: "FaceClusters",
                column: "LinkedAttendeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceDetections_FaceClusterId",
                table: "FaceDetections",
                column: "FaceClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceDetections_PhotoId",
                table: "FaceDetections",
                column: "PhotoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceDetections");

            migrationBuilder.DropTable(
                name: "FaceClusters");

            migrationBuilder.DropColumn(
                name: "FaceProcessingStatus",
                table: "Photos");
        }
    }
}
