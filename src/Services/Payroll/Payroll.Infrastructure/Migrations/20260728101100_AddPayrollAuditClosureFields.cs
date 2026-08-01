using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAuditClosureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "PayrollAuditRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedBy",
                table: "PayrollAuditRecords",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "PayrollAuditRecords");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "PayrollAuditRecords");
        }
    }
}
