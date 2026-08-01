using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Health.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthRetirementRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Documents = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthRetirementRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthStatusEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatusCode = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthStatusEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsuranceCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberNumber = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FamilyMembers = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsuranceContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    CoverageDetails = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistory = table.Column<string>(type: "text", nullable: true),
                    CurrentConditions = table.Column<string>(type: "text", nullable: true),
                    Allergies = table.Column<string>(type: "text", nullable: true),
                    Disabilities = table.Column<string>(type: "text", nullable: true),
                    LastExamDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeriodicMedicalChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Vitals = table.Column<string>(type: "text", nullable: true),
                    NextDue = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodicMedicalChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicalCommitteeReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitteeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Findings = table.Column<string>(type: "text", nullable: true),
                    Recommendation = table.Column<int>(type: "integer", nullable: false),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    RelatedDocuments = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalCommitteeReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalCommitteeReports_MedicalRecords_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "MedicalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicalRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationType = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalRecommendations_MedicalCommitteeReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "MedicalCommitteeReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalCommitteeReports_MedicalRecordId",
                table: "MedicalCommitteeReports",
                column: "MedicalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecommendations_ReportId",
                table: "MedicalRecommendations",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthRetirementRecords");

            migrationBuilder.DropTable(
                name: "HealthStatusEntries");

            migrationBuilder.DropTable(
                name: "InsuranceCards");

            migrationBuilder.DropTable(
                name: "InsuranceContracts");

            migrationBuilder.DropTable(
                name: "MedicalRecommendations");

            migrationBuilder.DropTable(
                name: "PeriodicMedicalChecks");

            migrationBuilder.DropTable(
                name: "MedicalCommitteeReports");

            migrationBuilder.DropTable(
                name: "MedicalRecords");
        }
    }
}
