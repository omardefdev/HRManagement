using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Health.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOccupationalSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirstAidTrainingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TrainingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CertificateNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirstAidTrainingRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SafetyInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InspectionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Inspector = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    Findings = table.Column<string>(type: "text", nullable: true),
                    NextDue = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyInspections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkplaceAccidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AccidentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    RelatedSickLeaveDays = table.Column<int>(type: "integer", nullable: false),
                    InvestigationNotes = table.Column<string>(type: "text", nullable: true),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkplaceAccidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirstAidTrainingRecords_EmployeeId",
                table: "FirstAidTrainingRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyInspections_InspectionDate",
                table: "SafetyInspections",
                column: "InspectionDate");

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceAccidents_AccidentDate",
                table: "WorkplaceAccidents",
                column: "AccidentDate");

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceAccidents_EmployeeId",
                table: "WorkplaceAccidents",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirstAidTrainingRecords");

            migrationBuilder.DropTable(
                name: "SafetyInspections");

            migrationBuilder.DropTable(
                name: "WorkplaceAccidents");
        }
    }
}
