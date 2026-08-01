using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compliance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_ComplaintFK_AssignedTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedTo",
                table: "LegalCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ComplaintId",
                table: "DisciplinaryActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplinaryActions_ComplaintId",
                table: "DisciplinaryActions",
                column: "ComplaintId");

            migrationBuilder.AddForeignKey(
                name: "FK_DisciplinaryActions_Complaints_ComplaintId",
                table: "DisciplinaryActions",
                column: "ComplaintId",
                principalTable: "Complaints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisciplinaryActions_Complaints_ComplaintId",
                table: "DisciplinaryActions");

            migrationBuilder.DropIndex(
                name: "IX_DisciplinaryActions_ComplaintId",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "LegalCases");

            migrationBuilder.DropColumn(
                name: "ComplaintId",
                table: "DisciplinaryActions");
        }
    }
}
