using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FileIdColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateUrl",
                table: "TrainingEnrollments");

            migrationBuilder.RenameColumn(
                name: "ContentUrl",
                table: "Lessons",
                newName: "ExternalResourceUrl");

            migrationBuilder.AddColumn<Guid>(
                name: "CertificateFileId",
                table: "TrainingEnrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContentFileId",
                table: "Lessons",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateFileId",
                table: "TrainingEnrollments");

            migrationBuilder.DropColumn(
                name: "ContentFileId",
                table: "Lessons");

            migrationBuilder.RenameColumn(
                name: "ExternalResourceUrl",
                table: "Lessons",
                newName: "ContentUrl");

            migrationBuilder.AddColumn<string>(
                name: "CertificateUrl",
                table: "TrainingEnrollments",
                type: "text",
                nullable: true);
        }
    }
}
