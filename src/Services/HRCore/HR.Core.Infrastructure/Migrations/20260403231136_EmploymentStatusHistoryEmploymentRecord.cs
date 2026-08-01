using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmploymentStatusHistoryEmploymentRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmploymentRecordId",
                table: "EmploymentStatusHistories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentStatusHistories_EmploymentRecordId",
                table: "EmploymentStatusHistories",
                column: "EmploymentRecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentStatusHistories_EmploymentRecords_EmploymentRecor~",
                table: "EmploymentStatusHistories",
                column: "EmploymentRecordId",
                principalTable: "EmploymentRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentStatusHistories_EmploymentRecords_EmploymentRecor~",
                table: "EmploymentStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_EmploymentStatusHistories_EmploymentRecordId",
                table: "EmploymentStatusHistories");

            migrationBuilder.DropColumn(
                name: "EmploymentRecordId",
                table: "EmploymentStatusHistories");
        }
    }
}
