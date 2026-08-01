using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GOV002_LegalDecisionRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ExportJobs is created by 20260728200000_PERF004_ExportJobs — do not recreate it here.
            migrationBuilder.CreateTable(
                name: "LegalAttestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AttestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecisionIdsJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalAttestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalAttestations_PayrollConfigurations_PayrollConfiguratio~",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalDecisionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfigField = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OfficialSource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IssueRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegimeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConfigContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDecisionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollConfigChangeNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupersedesConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImpactSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Channel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollConfigChangeNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollConfigChangeNotices_PayrollConfigurations_PayrollCon~",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalAttestations_PayrollConfigurationId_ContentHash",
                table: "LegalAttestations",
                columns: new[] { "PayrollConfigurationId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDecisionRecords_DecisionId",
                table: "LegalDecisionRecords",
                column: "DecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigChangeNotices_PayrollConfigurationId",
                table: "PayrollConfigChangeNotices",
                column: "PayrollConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalAttestations");

            migrationBuilder.DropTable(
                name: "LegalDecisionRecords");

            migrationBuilder.DropTable(
                name: "PayrollConfigChangeNotices");
        }
    }
}
