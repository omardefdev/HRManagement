using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EST006_JobCatalogImportBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobCatalogImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImportMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImportedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EstablishmentChangeRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCatalogImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCatalogImportBatches_EstablishmentChangeRequests_Establi~",
                        column: x => x.EstablishmentChangeRequestId,
                        principalTable: "EstablishmentChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "JobCatalogImportLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JobClassificationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LegacyJobCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ValidationError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCatalogImportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCatalogImportLines_JobCatalogImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "JobCatalogImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogImportBatches_EstablishmentChangeRequestId",
                table: "JobCatalogImportBatches",
                column: "EstablishmentChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogImportBatches_TenantId_ImportedAt",
                table: "JobCatalogImportBatches",
                columns: new[] { "TenantId", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogImportLines_BatchId",
                table: "JobCatalogImportLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogImportLines_TenantId_BatchId_LineNumber",
                table: "JobCatalogImportLines",
                columns: new[] { "TenantId", "BatchId", "LineNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobCatalogImportLines");

            migrationBuilder.DropTable(
                name: "JobCatalogImportBatches");
        }
    }
}
