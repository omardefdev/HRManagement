using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EST004_EstablishmentPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "WorkforcePlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "WorkforcePlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalSourceId",
                table: "WorkforcePlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinanceBudgetImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImportedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBudgetImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkforcePlanLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkforcePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    JobCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedCount = table.Column<int>(type: "integer", nullable: false),
                    PlannedFTE = table.Column<decimal>(type: "numeric", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkforcePlanLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkforcePlanLines_JobCatalogEntries_JobCatalogEntryId",
                        column: x => x.JobCatalogEntryId,
                        principalTable: "JobCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkforcePlanLines_WorkforcePlans_WorkforcePlanId",
                        column: x => x.WorkforcePlanId,
                        principalTable: "WorkforcePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceBudgetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ApprovedCount = table.Column<int>(type: "integer", nullable: false),
                    ApprovedFTE = table.Column<decimal>(type: "numeric", nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceBudgetLines_FinanceBudgetImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "FinanceBudgetImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBudgetImportBatches_TenantId_FiscalYear_ImportedAt",
                table: "FinanceBudgetImportBatches",
                columns: new[] { "TenantId", "FiscalYear", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBudgetLines_BatchId",
                table: "FinanceBudgetLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBudgetLines_TenantId_BatchId_CostCenter",
                table: "FinanceBudgetLines",
                columns: new[] { "TenantId", "BatchId", "CostCenter" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkforcePlanLines_JobCatalogEntryId",
                table: "WorkforcePlanLines",
                column: "JobCatalogEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkforcePlanLines_TenantId_WorkforcePlanId",
                table: "WorkforcePlanLines",
                columns: new[] { "TenantId", "WorkforcePlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkforcePlanLines_WorkforcePlanId",
                table: "WorkforcePlanLines",
                column: "WorkforcePlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceBudgetLines");

            migrationBuilder.DropTable(
                name: "WorkforcePlanLines");

            migrationBuilder.DropTable(
                name: "FinanceBudgetImportBatches");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "WorkforcePlans");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "WorkforcePlans");

            migrationBuilder.DropColumn(
                name: "LegalSourceId",
                table: "WorkforcePlans");
        }
    }
}
