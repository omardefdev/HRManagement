using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EST003_EstablishmentTemporal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFrom",
                table: "EstablishmentChangeRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FundedPositionVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundedPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FTE = table.Column<decimal>(type: "numeric", nullable: false),
                    BudgetYear = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccupantEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CostCenter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FreezeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecisionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundedPositionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundedPositionVersions_FundedPositions_FundedPositionId",
                        column: x => x.FundedPositionId,
                        principalTable: "FundedPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobCatalogVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JobCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccupationalGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecisionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCatalogVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCatalogVersions_JobCatalogEntries_JobCatalogEntryId",
                        column: x => x.JobCatalogEntryId,
                        principalTable: "JobCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundedPositionVersions_FundedPositionId_ValidFrom",
                table: "FundedPositionVersions",
                columns: new[] { "FundedPositionId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FundedPositionVersions_TenantId_FundedPositionId_ValidFrom",
                table: "FundedPositionVersions",
                columns: new[] { "TenantId", "FundedPositionId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FundedPositionVersions_TenantId_OrgUnitId_ValidFrom",
                table: "FundedPositionVersions",
                columns: new[] { "TenantId", "OrgUnitId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogVersions_JobCatalogEntryId_ValidFrom",
                table: "JobCatalogVersions",
                columns: new[] { "JobCatalogEntryId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCatalogVersions_TenantId_JobCatalogEntryId_ValidFrom",
                table: "JobCatalogVersions",
                columns: new[] { "TenantId", "JobCatalogEntryId", "ValidFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundedPositionVersions");

            migrationBuilder.DropTable(
                name: "JobCatalogVersions");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "EstablishmentChangeRequests");
        }
    }
}
