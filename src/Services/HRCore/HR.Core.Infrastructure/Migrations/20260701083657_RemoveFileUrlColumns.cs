using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFileUrlColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateUrl",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "StorageUrl",
                table: "EmployeeDocuments");

            migrationBuilder.RenameColumn(
                name: "DocumentUrl",
                table: "ApprovedCVs",
                newName: "Notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "ApprovedCVs",
                newName: "DocumentUrl");

            migrationBuilder.AddColumn<string>(
                name: "CertificateUrl",
                table: "Qualifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "Employees",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageUrl",
                table: "EmployeeDocuments",
                type: "text",
                nullable: true);
        }
    }
}
