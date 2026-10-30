using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PROD001ab_ComprehensivePolicyEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyEnforcementLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FactsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionJson = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShadowDiffJson = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyEnforcementLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyFactsSchemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyFactsSchemas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicySimulationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactsJson = table.Column<string>(type: "text", nullable: false),
                    DecisionJson = table.Column<string>(type: "text", nullable: false),
                    RunByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicySimulationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyWorkflowBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkflowCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyWorkflowBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyTemplateVersions_PolicyTemplates_PolicyTemplateId",
                        column: x => x.PolicyTemplateId,
                        principalTable: "PolicyTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PolicyPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MakerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RolledBackFromVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyPublications_PolicyTemplateVersions_PolicyTemplateVer~",
                        column: x => x.PolicyTemplateVersionId,
                        principalTable: "PolicyTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    WhenJson = table.Column<string>(type: "text", nullable: false),
                    ThenJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyRules_PolicyTemplateVersions_PolicyTemplateVersionId",
                        column: x => x.PolicyTemplateVersionId,
                        principalTable: "PolicyTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SodRules",
                columns: new[] { "Id", "CheckerRole", "CreatedAt", "EntityType", "FinancialThreshold", "MakerRole", "PosterRole", "RequireDifferentActors", "RequiredApprovalCount", "ReverserRole" },
                values: new object[,]
                {
                    { new Guid("a1000001-0001-4000-8000-000000000016"), "hr-management-approver", new DateTime(2026, 7, 28, 16, 26, 11, 643, DateTimeKind.Utc).AddTicks(1491), "AssetAssignment", null, "admin-logistics", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000017"), "admin-logistics", new DateTime(2026, 7, 28, 16, 26, 11, 643, DateTimeKind.Utc).AddTicks(1494), "SupplyOrder", null, "admin-logistics", null, true, 1, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEnforcementLedgers_CorrelationId",
                table: "PolicyEnforcementLedgers",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEnforcementLedgers_Domain_OccurredAt",
                table: "PolicyEnforcementLedgers",
                columns: new[] { "Domain", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyFactsSchemas_Domain",
                table: "PolicyFactsSchemas",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyPublications_PolicyTemplateVersionId",
                table: "PolicyPublications",
                column: "PolicyTemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRules_PolicyTemplateVersionId",
                table: "PolicyRules",
                column: "PolicyTemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTemplates_TenantId_Domain_Scope",
                table: "PolicyTemplates",
                columns: new[] { "TenantId", "Domain", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTemplateVersions_PolicyTemplateId_VersionNumber",
                table: "PolicyTemplateVersions",
                columns: new[] { "PolicyTemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTemplateVersions_Status_EffectiveFrom",
                table: "PolicyTemplateVersions",
                columns: new[] { "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyWorkflowBindings_Domain_WorkflowCode",
                table: "PolicyWorkflowBindings",
                columns: new[] { "Domain", "WorkflowCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyEnforcementLedgers");

            migrationBuilder.DropTable(
                name: "PolicyFactsSchemas");

            migrationBuilder.DropTable(
                name: "PolicyPublications");

            migrationBuilder.DropTable(
                name: "PolicyRules");

            migrationBuilder.DropTable(
                name: "PolicySimulationRuns");

            migrationBuilder.DropTable(
                name: "PolicyWorkflowBindings");

            migrationBuilder.DropTable(
                name: "PolicyTemplateVersions");

            migrationBuilder.DropTable(
                name: "PolicyTemplates");

            migrationBuilder.DeleteData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000016"));

            migrationBuilder.DeleteData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000017"));
        }
    }
}
