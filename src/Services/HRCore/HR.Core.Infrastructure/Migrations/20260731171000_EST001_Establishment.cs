using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HR.Core.Infrastructure.Data;

#nullable enable

namespace HR.Core.Infrastructure.Migrations;

/// <summary>EST-001: Establishment positions — JobCatalogEntry + FundedPosition tables.</summary>
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731171000_EST001_Establishment")]
public partial class EST001_Establishment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JobCatalogEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                JobCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TitleAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OccupationalGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobCatalogEntries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FundedPositions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                PositionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                FTE = table.Column<decimal>(type: "numeric", nullable: false, defaultValue: 1.0m),
                BudgetYear = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                JobCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                OccupantEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                CostCenter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                FreezeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                FreezeDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FundedPositions", x => x.Id);
                table.ForeignKey(
                    name: "FK_FundedPositions_OrgUnits_OrgUnitId",
                    column: x => x.OrgUnitId,
                    principalTable: "OrgUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FundedPositions_JobCatalogEntries_JobCatalogEntryId",
                    column: x => x.JobCatalogEntryId,
                    principalTable: "JobCatalogEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_FundedPositions_Employees_OccupantEmployeeId",
                    column: x => x.OccupantEmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JobCatalogEntries_TenantId_JobCode",
            table: "JobCatalogEntries",
            columns: new[] { "TenantId", "JobCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FundedPositions_TenantId_PositionCode_BudgetYear",
            table: "FundedPositions",
            columns: new[] { "TenantId", "PositionCode", "BudgetYear" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FundedPositions_OrgUnitId",
            table: "FundedPositions",
            column: "OrgUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_FundedPositions_JobCatalogEntryId",
            table: "FundedPositions",
            column: "JobCatalogEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_FundedPositions_OccupantEmployeeId",
            table: "FundedPositions",
            column: "OccupantEmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_FundedPositions_TenantId_Status",
            table: "FundedPositions",
            columns: new[] { "TenantId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FundedPositions");
        migrationBuilder.DropTable(name: "JobCatalogEntries");
    }
}
