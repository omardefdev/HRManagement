using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_PayrollEngineAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunType",
                table: "PayrollRuns",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceRunId",
                table: "PayrollRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbsenceDeductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AbsentDays = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppliedInRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceDeductions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayProfileHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayProfileHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePayProfileHistories_EmployeePayProfiles_SourceProfi~",
                        column: x => x.SourceProfileId,
                        principalTable: "EmployeePayProfiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationMessageReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConsumerType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationMessageReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficialHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ProrationDaysBasis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DailyRateDivisor = table.Column<int>(type: "integer", nullable: false),
                    MissionAllowanceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SecondmentAllowancePercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EnableTrainingPremiums = table.Column<bool>(type: "boolean", nullable: false),
                    TrainingPremiumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxRetroCorrectionMonths = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunApprovals_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayslipDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalaryLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayslipDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayslipDeliveries_SalaryLines_SalaryLineId",
                        column: x => x.SalaryLineId,
                        principalTable: "SalaryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxBrackets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpperLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxBrackets_PayrollConfigurations_PayrollConfigurationId",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfileHistories_EmployeeId",
                table: "EmployeePayProfileHistories",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfileHistories_SourceProfileId",
                table: "EmployeePayProfileHistories",
                column: "SourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessageReceipts_MessageId_ConsumerType",
                table: "IntegrationMessageReceipts",
                columns: new[] { "MessageId", "ConsumerType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_Code",
                table: "PayrollConfigurations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunApprovals_PayrollRunId",
                table: "PayrollRunApprovals",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipDeliveries_SalaryLineId",
                table: "PayslipDeliveries",
                column: "SalaryLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxBrackets_PayrollConfigurationId",
                table: "TaxBrackets",
                column: "PayrollConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbsenceDeductions");

            migrationBuilder.DropTable(
                name: "EmployeePayProfileHistories");

            migrationBuilder.DropTable(
                name: "IntegrationMessageReceipts");

            migrationBuilder.DropTable(
                name: "OfficialHolidays");

            migrationBuilder.DropTable(
                name: "PayrollRunApprovals");

            migrationBuilder.DropTable(
                name: "PayslipDeliveries");

            migrationBuilder.DropTable(
                name: "TaxBrackets");

            migrationBuilder.DropTable(
                name: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "RunType",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "SourceRunId",
                table: "PayrollRuns");
        }
    }
}
