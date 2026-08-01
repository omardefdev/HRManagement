using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_SalaryScalesGradesDeductionsProfilesComponentsEOS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeductionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsStatutory = table.Column<bool>(type: "boolean", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FixedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeductionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EndOfServiceSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeparationRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceYears = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LeaveBalancePayout = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GratuityAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherCompensation = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSettlement = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfServiceSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalaryLineComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalaryLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ComponentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryLineComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryLineComponents_SalaryLines_SalaryLineId",
                        column: x => x.SalaryLineId,
                        principalTable: "SalaryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryScales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryScales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayGrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalaryScaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GradeNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MinSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayGrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayGrades_SalaryScales_SalaryScaleId",
                        column: x => x.SalaryScaleId,
                        principalTable: "SalaryScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePayProfiles_PayGrades_PayGradeId",
                        column: x => x.PayGradeId,
                        principalTable: "PayGrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeductionTypes_Code",
                table: "DeductionTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfiles_EmployeeId_IsActive",
                table: "EmployeePayProfiles",
                columns: new[] { "EmployeeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfiles_PayGradeId",
                table: "EmployeePayProfiles",
                column: "PayGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfServiceSettlements_EmployeeId",
                table: "EndOfServiceSettlements",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayGrades_SalaryScaleId_GradeCode",
                table: "PayGrades",
                columns: new[] { "SalaryScaleId", "GradeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLineComponents_SalaryLineId",
                table: "SalaryLineComponents",
                column: "SalaryLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryScales_Code",
                table: "SalaryScales",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeductionTypes");

            migrationBuilder.DropTable(
                name: "EmployeePayProfiles");

            migrationBuilder.DropTable(
                name: "EndOfServiceSettlements");

            migrationBuilder.DropTable(
                name: "SalaryLineComponents");

            migrationBuilder.DropTable(
                name: "PayGrades");

            migrationBuilder.DropTable(
                name: "SalaryScales");
        }
    }
}
