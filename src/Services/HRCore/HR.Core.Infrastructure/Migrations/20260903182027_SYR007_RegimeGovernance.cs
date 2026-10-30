using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR007_RegimeGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmploymentRegimeTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmploymentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransitionDecisionRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConditionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConflictNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentRegimeTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmploymentRegimeTransitions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmploymentRegimeTransitions_EmploymentRecords_EmploymentRec~",
                        column: x => x.EmploymentRecordId,
                        principalTable: "EmploymentRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRegimeExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmploymentRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExceptionType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SourceContext = table.Column<int>(type: "integer", nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptedRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRegimeExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRegimeExceptions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRegimeExceptions_EmploymentRecords_EmploymentRecordId",
                        column: x => x.EmploymentRecordId,
                        principalTable: "EmploymentRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRegimeTransitions_EmployeeId",
                table: "EmploymentRegimeTransitions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRegimeTransitions_EmploymentRecordId",
                table: "EmploymentRegimeTransitions",
                column: "EmploymentRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRegimeTransitions_Tenant_Employee_EffectiveFrom",
                table: "EmploymentRegimeTransitions",
                columns: new[] { "TenantId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRegimeExceptions_EmployeeId",
                table: "ServiceRegimeExceptions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRegimeExceptions_EmploymentRecordId",
                table: "ServiceRegimeExceptions",
                column: "EmploymentRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRegimeExceptions_Tenant_Employee_Type_Status",
                table: "ServiceRegimeExceptions",
                columns: new[] { "TenantId", "EmployeeId", "ExceptionType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRegimeExceptions_Tenant_Status",
                table: "ServiceRegimeExceptions",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmploymentRegimeTransitions");

            migrationBuilder.DropTable(
                name: "ServiceRegimeExceptions");
        }
    }
}
