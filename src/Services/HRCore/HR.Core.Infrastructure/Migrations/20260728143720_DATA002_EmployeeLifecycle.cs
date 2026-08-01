using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA002_EmployeeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedCVs_Employees_EmployeeId",
                table: "ApprovedCVs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeId",
                table: "EmployeeDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentRecords_Employees_EmployeeId",
                table: "EmploymentRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentStatusHistories_Employees_EmployeeId",
                table: "EmploymentStatusHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                table: "PerformanceEvaluations");

            migrationBuilder.DropForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications");

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "Employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldSetAt",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalHoldSetBy",
                table: "Employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoEmployeeId",
                table: "Employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangesJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequiresLegalApproval = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCorrections_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(7479));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8361));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8365));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8367));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(5183));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7314));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7323));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7326));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7328));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7330));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7331));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7333));

            migrationBuilder.CreateIndex(
                name: "IX_Employees_MergedIntoEmployeeId",
                table: "Employees",
                column: "MergedIntoEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCorrections_EmployeeId_Status",
                table: "EmployeeCorrections",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedCVs_Employees_EmployeeId",
                table: "ApprovedCVs",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeId",
                table: "EmployeeDocuments",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Employees_MergedIntoEmployeeId",
                table: "Employees",
                column: "MergedIntoEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentRecords_Employees_EmployeeId",
                table: "EmploymentRecords",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentStatusHistories_Employees_EmployeeId",
                table: "EmploymentStatusHistories",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                table: "PerformanceEvaluations",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedCVs_Employees_EmployeeId",
                table: "ApprovedCVs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeId",
                table: "EmployeeDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Employees_MergedIntoEmployeeId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentRecords_Employees_EmployeeId",
                table: "EmploymentRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentStatusHistories_Employees_EmployeeId",
                table: "EmploymentStatusHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                table: "PerformanceEvaluations");

            migrationBuilder.DropForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications");

            migrationBuilder.DropTable(
                name: "EmployeeCorrections");

            migrationBuilder.DropIndex(
                name: "IX_Employees_MergedIntoEmployeeId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LegalHold",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LegalHoldSetAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LegalHoldSetBy",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MergedIntoEmployeeId",
                table: "Employees");

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(1673));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2325));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2328));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2335));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 404, DateTimeKind.Utc).AddTicks(9932));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1536));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1543));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1546));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1547));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1549));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1555));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1557));

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedCVs_Employees_EmployeeId",
                table: "ApprovedCVs",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeId",
                table: "EmployeeDocuments",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentRecords_Employees_EmployeeId",
                table: "EmploymentRecords",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentStatusHistories_Employees_EmployeeId",
                table: "EmploymentStatusHistories",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                table: "PerformanceEvaluations",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
