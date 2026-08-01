using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileReferenceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "Qualifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhotoFileId",
                table: "Employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "EmployeeDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "ApprovedCVs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileId",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "PhotoFileId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "ApprovedCVs");
        }
    }
}
