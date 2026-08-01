using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GOV003_MasterDataDuplicateCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ExportJobs is created by 20260728200001_PERF004_ExportJobs — do not recreate it here.
            migrationBuilder.CreateTable(
                name: "MasterDataDuplicateCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CandidateEmployeeIdA = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateEmployeeIdB = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedIntoEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterDataDuplicateCases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MasterDataDuplicateCases_TenantId_Status_OpenedAt",
                table: "MasterDataDuplicateCases",
                columns: new[] { "TenantId", "Status", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterDataDuplicateCases");
        }
    }
}
