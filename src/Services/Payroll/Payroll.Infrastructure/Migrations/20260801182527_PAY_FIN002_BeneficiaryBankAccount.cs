using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY_FIN002_BeneficiaryBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountIban",
                table: "EmployeePayProfiles",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BankAccountVerifiedAt",
                table: "EmployeePayProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "EmployeePayProfiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountIban",
                table: "EmployeePayProfiles");

            migrationBuilder.DropColumn(
                name: "BankAccountVerifiedAt",
                table: "EmployeePayProfiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "EmployeePayProfiles");
        }
    }
}
