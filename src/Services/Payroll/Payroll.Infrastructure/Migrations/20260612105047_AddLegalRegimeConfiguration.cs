using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalRegimeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EosGratuityFirstYearsThreshold",
                table: "PayrollConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EosGratuityRateAfterThreshold",
                table: "PayrollConfigurations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EosGratuityRateFirstPeriod",
                table: "PayrollConfigurations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LeavePayoutDailyDivisor",
                table: "PayrollConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "PayrollConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NonWorkingDays",
                table: "PayrollConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegimeCode",
                table: "PayrollConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxExemptionAmount",
                table: "PayrollConfigurations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxableBaseMode",
                table: "PayrollConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "UseDbHolidaysOnly",
                table: "PayrollConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollConfigurationId",
                table: "OfficialHolidays",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_RegimeCode",
                table: "PayrollConfigurations",
                column: "RegimeCode");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialHolidays_PayrollConfigurationId",
                table: "OfficialHolidays",
                column: "PayrollConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_OfficialHolidays_PayrollConfigurations_PayrollConfiguration~",
                table: "OfficialHolidays",
                column: "PayrollConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OfficialHolidays_PayrollConfigurations_PayrollConfiguration~",
                table: "OfficialHolidays");

            migrationBuilder.DropIndex(
                name: "IX_PayrollConfigurations_RegimeCode",
                table: "PayrollConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_OfficialHolidays_PayrollConfigurationId",
                table: "OfficialHolidays");

            migrationBuilder.DropColumn(
                name: "EosGratuityFirstYearsThreshold",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "EosGratuityRateAfterThreshold",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "EosGratuityRateFirstPeriod",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "LeavePayoutDailyDivisor",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "NonWorkingDays",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "RegimeCode",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "TaxExemptionAmount",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "TaxableBaseMode",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "UseDbHolidaysOnly",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "PayrollConfigurationId",
                table: "OfficialHolidays");
        }
    }
}
