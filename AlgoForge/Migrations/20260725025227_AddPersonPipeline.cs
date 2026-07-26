using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlgoForge.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_FaceDetections_FaceDetectionId",
                table: "Tags");

            // Detections from the old face-only pipeline cannot be mapped onto the new
            // person detections: different algorithm, different rows, different ids. Any
            // existing tag therefore points at a row that is about to disappear, and the
            // foreign key added at the end of this migration would fail on it.
            //
            // This is a hard delete of consent records, which working rule 4 otherwise
            // forbids. It is defensible only because these are pre-pipeline development
            // tags whose referent no longer exists -- re-running detection and
            // identification regenerates them. Do not apply this migration to a database
            // holding real attendee decisions.
            migrationBuilder.Sql("DELETE FROM Tags;");

            migrationBuilder.DropTable(
                name: "FaceDetections");

            migrationBuilder.DropTable(
                name: "FaceClusters");

            migrationBuilder.RenameColumn(
                name: "FaceDetectionId",
                table: "Tags",
                newName: "PersonDetectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Tags_FaceDetectionId",
                table: "Tags",
                newName: "IX_Tags_PersonDetectionId");

            migrationBuilder.CreateTable(
                name: "PersonClusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AnchorDetectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HasTaggableDetection = table.Column<bool>(type: "bit", nullable: false),
                    LinkedAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdentifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonClusters_AspNetUsers_IdentifiedByUserId",
                        column: x => x.IdentifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonClusters_Attendees_LinkedAttendeeId",
                        column: x => x.LinkedAttendeeId,
                        principalTable: "Attendees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonClusters_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonDetections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonClusterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoxX = table.Column<double>(type: "float", nullable: false),
                    BoxY = table.Column<double>(type: "float", nullable: false),
                    BoxWidth = table.Column<double>(type: "float", nullable: false),
                    BoxHeight = table.Column<double>(type: "float", nullable: false),
                    FaceX = table.Column<double>(type: "float", nullable: true),
                    FaceY = table.Column<double>(type: "float", nullable: true),
                    FaceWidth = table.Column<double>(type: "float", nullable: true),
                    FaceHeight = table.Column<double>(type: "float", nullable: true),
                    FaceQuality = table.Column<double>(type: "float", nullable: false),
                    Sharpness = table.Column<double>(type: "float", nullable: false),
                    ProminenceScore = table.Column<double>(type: "float", nullable: false),
                    IsTaggable = table.Column<bool>(type: "bit", nullable: false),
                    FaceEmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppearanceEmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeadEmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClusterConfidence = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonDetections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonDetections_PersonClusters_PersonClusterId",
                        column: x => x.PersonClusterId,
                        principalTable: "PersonClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PersonDetections_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonClusters_EventId_Status",
                table: "PersonClusters",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonClusters_IdentifiedByUserId",
                table: "PersonClusters",
                column: "IdentifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonClusters_LinkedAttendeeId",
                table: "PersonClusters",
                column: "LinkedAttendeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonDetections_PersonClusterId",
                table: "PersonDetections",
                column: "PersonClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonDetections_PhotoId_IsTaggable",
                table: "PersonDetections",
                columns: new[] { "PhotoId", "IsTaggable" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_PersonDetections_PersonDetectionId",
                table: "Tags",
                column: "PersonDetectionId",
                principalTable: "PersonDetections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_PersonDetections_PersonDetectionId",
                table: "Tags");

            migrationBuilder.DropTable(
                name: "PersonDetections");

            migrationBuilder.DropTable(
                name: "PersonClusters");

            migrationBuilder.RenameColumn(
                name: "PersonDetectionId",
                table: "Tags",
                newName: "FaceDetectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Tags_PersonDetectionId",
                table: "Tags",
                newName: "IX_Tags_FaceDetectionId");

            migrationBuilder.CreateTable(
                name: "FaceClusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RepresentativeEmbeddingRef = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceClusters_AspNetUsers_IdentifiedByUserId",
                        column: x => x.IdentifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FaceClusters_Attendees_LinkedAttendeeId",
                        column: x => x.LinkedAttendeeId,
                        principalTable: "Attendees",
                        principalColumn: "Id");
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
                    FaceClusterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoxHeight = table.Column<double>(type: "float", nullable: false),
                    BoxWidth = table.Column<double>(type: "float", nullable: false),
                    BoxX = table.Column<double>(type: "float", nullable: false),
                    BoxY = table.Column<double>(type: "float", nullable: false),
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
                        principalColumn: "Id");
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

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_FaceDetections_FaceDetectionId",
                table: "Tags",
                column: "FaceDetectionId",
                principalTable: "FaceDetections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
