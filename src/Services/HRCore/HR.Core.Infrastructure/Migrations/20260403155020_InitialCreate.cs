using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovedCVs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentUrl = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedCVs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocType = table.Column<int>(type: "integer", nullable: false),
                    DocTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StorageUrl = table.Column<string>(type: "text", nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NationalIdIssuePlace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResidentialAddress = table.Column<string>(type: "text", nullable: true),
                    PermanentAddress = table.Column<string>(type: "text", nullable: true),
                    EmergencyContact = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NationalId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BirthDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    MaritalStatus = table.Column<int>(type: "integer", nullable: false),
                    PrimaryContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStatus = table.Column<int>(type: "integer", nullable: false),
                    PrimaryOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    HireDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmploymentStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmploymentStatusHistories_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeadEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgUnits_Employees_HeadEmployeeId",
                        column: x => x.HeadEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrgUnits_OrgUnits_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerformanceEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    EvaluatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Qualifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualificationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Provider = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CompletionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CertificateUrl = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Qualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Qualifications_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmploymentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PositionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmploymentType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmploymentRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmploymentRecords_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedCVs_EmployeeId",
                table: "ApprovedCVs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactInfos_EmployeeId",
                table: "ContactInfos",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeId",
                table: "EmployeeDocuments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProfiles_EmployeeId",
                table: "EmployeeProfiles",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NationalId",
                table: "Employees",
                column: "NationalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_PrimaryOrgUnitId",
                table: "Employees",
                column: "PrimaryOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecords_EmployeeId",
                table: "EmploymentRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecords_OrgUnitId",
                table: "EmploymentRecords",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentStatusHistories_EmployeeId",
                table: "EmploymentStatusHistories",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_Code",
                table: "OrgUnits",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_HeadEmployeeId",
                table: "OrgUnits",
                column: "HeadEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_ParentId",
                table: "OrgUnits",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceEvaluations_EmployeeId",
                table: "PerformanceEvaluations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Qualifications_EmployeeId",
                table: "Qualifications",
                column: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedCVs_Employees_EmployeeId",
                table: "ApprovedCVs",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContactInfos_Employees_EmployeeId",
                table: "ContactInfos",
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
                name: "FK_EmployeeProfiles_Employees_EmployeeId",
                table: "EmployeeProfiles",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_OrgUnits_PrimaryOrgUnitId",
                table: "Employees",
                column: "PrimaryOrgUnitId",
                principalTable: "OrgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgUnits_Employees_HeadEmployeeId",
                table: "OrgUnits");

            migrationBuilder.DropTable(
                name: "ApprovedCVs");

            migrationBuilder.DropTable(
                name: "ContactInfos");

            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "EmployeeProfiles");

            migrationBuilder.DropTable(
                name: "EmploymentRecords");

            migrationBuilder.DropTable(
                name: "EmploymentStatusHistories");

            migrationBuilder.DropTable(
                name: "PerformanceEvaluations");

            migrationBuilder.DropTable(
                name: "Qualifications");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "OrgUnits");
        }
    }
}
