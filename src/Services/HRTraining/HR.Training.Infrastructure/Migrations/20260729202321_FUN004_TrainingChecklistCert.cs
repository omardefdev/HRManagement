using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN004_TrainingChecklistCert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "TrainingEnrollments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "TrainingEnrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateNo",
                table: "TrainingEnrollments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "LearningCertificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "LearningCertificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrainingChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SlaHours = table.Column<int>(type: "integer", nullable: false),
                    EvidenceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingChecklists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingChecklists_EnrollmentId_Step",
                table: "TrainingChecklists",
                columns: new[] { "EnrollmentId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingChecklists");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "TrainingEnrollments");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "TrainingEnrollments");

            migrationBuilder.DropColumn(
                name: "CertificateNo",
                table: "TrainingEnrollments");

            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "LearningCertificates");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "LearningCertificates");
        }
    }
}
