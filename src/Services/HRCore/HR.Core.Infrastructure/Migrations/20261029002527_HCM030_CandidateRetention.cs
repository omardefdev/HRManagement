using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_CandidateRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateRetentionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    DueCount = table.Column<int>(type: "integer", nullable: false),
                    AnonymizedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedLegalHold = table.Column<int>(type: "integer", nullable: false),
                    SkippedAppeal = table.Column<int>(type: "integer", nullable: false),
                    TriggeredBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateRetentionRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateRetentionRuns_TenantId_StartedAt",
                table: "CandidateRetentionRuns",
                columns: new[] { "TenantId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateRetentionRuns");
        }
    }
}
