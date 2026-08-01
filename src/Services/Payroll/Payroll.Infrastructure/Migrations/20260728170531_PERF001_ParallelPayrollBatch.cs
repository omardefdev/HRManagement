using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PERF001_ParallelPayrollBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkerId",
                table: "PayrollRunPartitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CalculateStartedAt",
                table: "PayrollRunJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EstimatedCompletionAt",
                table: "PayrollRunJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedEmployeeCount",
                table: "PayrollRunJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalEmployeeCount",
                table: "PayrollRunJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InputFingerprint",
                table: "PayrollRunInputSnapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "PayrollRunPartitions");

            migrationBuilder.DropColumn(
                name: "CalculateStartedAt",
                table: "PayrollRunJobs");

            migrationBuilder.DropColumn(
                name: "EstimatedCompletionAt",
                table: "PayrollRunJobs");

            migrationBuilder.DropColumn(
                name: "ProcessedEmployeeCount",
                table: "PayrollRunJobs");

            migrationBuilder.DropColumn(
                name: "TotalEmployeeCount",
                table: "PayrollRunJobs");

            migrationBuilder.DropColumn(
                name: "InputFingerprint",
                table: "PayrollRunInputSnapshots");
        }
    }
}
