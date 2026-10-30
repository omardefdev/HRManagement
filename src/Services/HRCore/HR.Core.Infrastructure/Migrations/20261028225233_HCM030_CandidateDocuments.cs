using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_CandidateDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequiredDocumentTypes",
                table: "RecruitmentRequisitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CandidateDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CitizenApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateDocuments_CitizenApplications_CitizenApplicationId",
                        column: x => x.CitizenApplicationId,
                        principalTable: "CitizenApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CandidateDocuments_RecruitmentApplications_RecruitmentAppli~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateDocuments_CitizenApplicationId",
                table: "CandidateDocuments",
                column: "CitizenApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateDocuments_RecruitmentApplicationId",
                table: "CandidateDocuments",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateDocuments_TenantId_FileId",
                table: "CandidateDocuments",
                columns: new[] { "TenantId", "FileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateDocuments_TenantId_RecruitmentApplicationId_Upload~",
                table: "CandidateDocuments",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "UploadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateDocuments");

            migrationBuilder.DropColumn(
                name: "RequiredDocumentTypes",
                table: "RecruitmentRequisitions");
        }
    }
}
