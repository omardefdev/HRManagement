using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class COD001_CodeGenerationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeIssuances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SequenceScope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FactsJson = table.Column<string>(type: "text", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeIssuances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodePolicyTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodePolicyTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodeSequenceCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeSequenceCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodePolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodePolicyTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SegmentsJson = table.Column<string>(type: "text", nullable: false),
                    SequenceReset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FallbackPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodePolicyVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodePolicyVersions_CodePolicyTemplates_CodePolicyTemplateId",
                        column: x => x.CodePolicyTemplateId,
                        principalTable: "CodePolicyTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodePolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodePolicyTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodePolicyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    WhenJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodePolicyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodePolicyRules_CodePolicyTemplates_CodePolicyTemplateId",
                        column: x => x.CodePolicyTemplateId,
                        principalTable: "CodePolicyTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodePolicyRules_CodePolicyVersions_CodePolicyVersionId",
                        column: x => x.CodePolicyVersionId,
                        principalTable: "CodePolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeIssuances_IssuedAt",
                table: "CodeIssuances",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CodeIssuances_TenantId_EntityKind_Code",
                table: "CodeIssuances",
                columns: new[] { "TenantId", "EntityKind", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodePolicyRules_CodePolicyTemplateId",
                table: "CodePolicyRules",
                column: "CodePolicyTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CodePolicyRules_CodePolicyVersionId",
                table: "CodePolicyRules",
                column: "CodePolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CodePolicyTemplates_TenantId_EntityKind_Scope",
                table: "CodePolicyTemplates",
                columns: new[] { "TenantId", "EntityKind", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_CodePolicyVersions_CodePolicyTemplateId_VersionNumber",
                table: "CodePolicyVersions",
                columns: new[] { "CodePolicyTemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodePolicyVersions_Status_EffectiveFrom",
                table: "CodePolicyVersions",
                columns: new[] { "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeSequenceCounters_TenantId_EntityKind_ScopeKey_Year",
                table: "CodeSequenceCounters",
                columns: new[] { "TenantId", "EntityKind", "ScopeKey", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeIssuances");

            migrationBuilder.DropTable(
                name: "CodePolicyRules");

            migrationBuilder.DropTable(
                name: "CodeSequenceCounters");

            migrationBuilder.DropTable(
                name: "CodePolicyVersions");

            migrationBuilder.DropTable(
                name: "CodePolicyTemplates");
        }
    }
}
