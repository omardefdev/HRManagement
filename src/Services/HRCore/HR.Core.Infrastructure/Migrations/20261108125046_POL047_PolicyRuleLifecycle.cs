using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class POL047_PolicyRuleLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                table: "PolicyTemplateVersions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RollbackOfVersionId",
                table: "PolicyTemplateVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WithdrawsAllRules",
                table: "PolicyTemplateVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PolicyTemplates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PolicyTemplateVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);


            migrationBuilder.AddColumn<string>(
                name: "CheckerRole",
                table: "PolicyPublications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PolicyPublications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "MakerRole",
                table: "PolicyPublications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluatedAsOf",
                table: "PolicyEnforcementLedgers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PolicySeedApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeSetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PolicyTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicySeedApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicySeedApplications_PolicyTemplates_PolicyTemplateId",
                        column: x => x.PolicyTemplateId,
                        principalTable: "PolicyTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PolicyVersionEdits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyVersionEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyVersionEdits_PolicyTemplateVersions_PolicyTemplateVer~",
                        column: x => x.PolicyTemplateVersionId,
                        principalTable: "PolicyTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTemplateVersions_OneDraftPerTemplate",
                table: "PolicyTemplateVersions",
                column: "PolicyTemplateId",
                unique: true,
                filter: "\"Status\" = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "IX_PolicySeedApplications_ChangeSetId_PolicyTemplateId",
                table: "PolicySeedApplications",
                columns: new[] { "ChangeSetId", "PolicyTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicySeedApplications_PolicyTemplateId",
                table: "PolicySeedApplications",
                column: "PolicyTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersionEdits_PolicyTemplateVersionId_OccurredAt",
                table: "PolicyVersionEdits",
                columns: new[] { "PolicyTemplateVersionId", "OccurredAt" });

            // PLAN-47 (G-POL-07, P-3): a published version never changes, not even through ExecuteUpdate or raw SQL. The rule guard
            // looks the version up as the invoking role (SECURITY INVOKER): row security's WITH CHECK holds a rule write to the
            // session's tenant, which is the rule's tenant and its version's, so the version is visible. A version it cannot see is
            // refused (fail closed), except for a DELETE whose version is already gone (the cascade of a deleted draft).
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(PolicyImmutabilitySql);
            }
        }

        /// <summary>The functions and triggers; Down drops them (the SQL to drop them by hand is in OPS_VPS_SINGLE_HOST.md).</summary>
        internal const string PolicyImmutabilitySql = """
            CREATE OR REPLACE FUNCTION public.policy_rules_guard() RETURNS trigger
            LANGUAGE plpgsql SECURITY INVOKER AS $fn$
            DECLARE
                v_status text;
            BEGIN
                IF TG_OP IN ('UPDATE', 'DELETE') THEN
                    SELECT v."Status" INTO v_status FROM public."PolicyTemplateVersions" v WHERE v."Id" = OLD."PolicyTemplateVersionId" FOR SHARE;
                    IF (TG_OP = 'UPDATE' AND v_status IS DISTINCT FROM 'Draft')
                       OR (TG_OP = 'DELETE' AND v_status IS NOT NULL AND v_status <> 'Draft') THEN
                        RAISE EXCEPTION 'PLAN-47 policy immutability: the rules of policy version % (status %) cannot change; create a new draft',
                            OLD."PolicyTemplateVersionId", COALESCE(v_status, 'not visible')
                            USING ERRCODE = 'P0047';
                    END IF;
                END IF;

                IF TG_OP IN ('INSERT', 'UPDATE') THEN
                    SELECT v."Status" INTO v_status FROM public."PolicyTemplateVersions" v WHERE v."Id" = NEW."PolicyTemplateVersionId" FOR SHARE;
                    IF v_status IS DISTINCT FROM 'Draft' THEN
                        RAISE EXCEPTION 'PLAN-47 policy immutability: the rules of policy version % (status %) cannot change; create a new draft',
                            NEW."PolicyTemplateVersionId", COALESCE(v_status, 'not visible')
                            USING ERRCODE = 'P0047';
                    END IF;
                    RETURN NEW;
                END IF;

                RETURN OLD;
            END
            $fn$;

            CREATE OR REPLACE FUNCTION public.policy_versions_guard() RETURNS trigger
            LANGUAGE plpgsql SECURITY INVOKER AS $fn$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    IF OLD."Status" <> 'Draft' THEN
                        RAISE EXCEPTION 'PLAN-47 policy immutability: policy version % (status %) cannot be deleted', OLD."Id", OLD."Status"
                            USING ERRCODE = 'P0047';
                    END IF;
                    RETURN OLD;
                END IF;

                IF OLD."Status" <> 'Draft'
                   AND (to_jsonb(NEW) - 'EffectiveTo' - 'UpdatedAt' - 'UpdatedBy')
                       IS DISTINCT FROM (to_jsonb(OLD) - 'EffectiveTo' - 'UpdatedAt' - 'UpdatedBy') THEN
                    RAISE EXCEPTION 'PLAN-47 policy immutability: policy version % (status %) changes only in EffectiveTo, UpdatedAt and UpdatedBy', OLD."Id", OLD."Status"
                        USING ERRCODE = 'P0047';
                END IF;
                RETURN NEW;
            END
            $fn$;

            DROP TRIGGER IF EXISTS policy_rules_guard ON public."PolicyRules";
            CREATE TRIGGER policy_rules_guard
                BEFORE INSERT OR UPDATE OR DELETE ON public."PolicyRules"
                FOR EACH ROW EXECUTE FUNCTION public.policy_rules_guard();

            DROP TRIGGER IF EXISTS policy_versions_guard ON public."PolicyTemplateVersions";
            CREATE TRIGGER policy_versions_guard
                BEFORE UPDATE OR DELETE ON public."PolicyTemplateVersions"
                FOR EACH ROW EXECUTE FUNCTION public.policy_versions_guard();
            """;

        internal const string DropPolicyImmutabilitySql = """
            DROP TRIGGER IF EXISTS policy_rules_guard ON public."PolicyRules";
            DROP TRIGGER IF EXISTS policy_versions_guard ON public."PolicyTemplateVersions";
            DROP FUNCTION IF EXISTS public.policy_rules_guard();
            DROP FUNCTION IF EXISTS public.policy_versions_guard();
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(DropPolicyImmutabilitySql);
            }

            migrationBuilder.DropTable(
                name: "PolicySeedApplications");

            migrationBuilder.DropTable(
                name: "PolicyVersionEdits");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTemplateVersions_OneDraftPerTemplate",
                table: "PolicyTemplateVersions");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                table: "PolicyTemplateVersions");

            migrationBuilder.DropColumn(
                name: "RollbackOfVersionId",
                table: "PolicyTemplateVersions");

            migrationBuilder.DropColumn(
                name: "WithdrawsAllRules",
                table: "PolicyTemplateVersions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PolicyTemplates");
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PolicyTemplateVersions");

            migrationBuilder.DropColumn(
                name: "CheckerRole",
                table: "PolicyPublications");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PolicyPublications");

            migrationBuilder.DropColumn(
                name: "MakerRole",
                table: "PolicyPublications");

            migrationBuilder.DropColumn(
                name: "EvaluatedAsOf",
                table: "PolicyEnforcementLedgers");
        }
    }
}
