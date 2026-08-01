using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditSequenceNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SequenceNumber",
                table: "gateway_audit_entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_gateway_audit_entries_SequenceNumber",
                table: "gateway_audit_entries",
                column: "SequenceNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gateway_audit_entries_SequenceNumber",
                table: "gateway_audit_entries");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "gateway_audit_entries");
        }
    }
}
