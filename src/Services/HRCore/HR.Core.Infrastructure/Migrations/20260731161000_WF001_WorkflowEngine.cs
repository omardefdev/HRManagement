using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

/// <summary>WF-001: Configurable Workflow Engine — tables for definitions, instances, and delegations.</summary>
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731161000_WF001_WorkflowEngine")]
public partial class WF001_WorkflowEngine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WorkflowDefinitions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TenantId = table.Column<Guid>(nullable: false),
                Code = table.Column<string>(maxLength: 50, nullable: false),
                Name = table.Column<string>(maxLength: 200, nullable: false),
                Version = table.Column<int>(nullable: false, defaultValue: 1),
                ValidFrom = table.Column<DateTime>(nullable: true),
                ValidTo = table.Column<DateTime>(nullable: true),
                IsPublished = table.Column<bool>(nullable: false, defaultValue: false),
                DomainEntityType = table.Column<string>(maxLength: 100, nullable: false),
                StepsJson = table.Column<string>(nullable: false, defaultValue: "[]"),
                GuardrailRolesJson = table.Column<string>(nullable: false, defaultValue: "[]"),
                Description = table.Column<string>(maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowDefinitions_Code_Version",
            table: "WorkflowDefinitions",
            columns: new[] { "Code", "Version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowDefinitions_DomainEntityType_IsPublished",
            table: "WorkflowDefinitions",
            columns: new[] { "DomainEntityType", "IsPublished" });

        migrationBuilder.CreateTable(
            name: "WorkflowInstances",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TenantId = table.Column<Guid>(nullable: false),
                DefinitionId = table.Column<Guid>(nullable: false),
                DefinitionVersion = table.Column<int>(nullable: false),
                EntityType = table.Column<string>(maxLength: 100, nullable: false),
                EntityId = table.Column<Guid>(nullable: false),
                CurrentStepLevel = table.Column<int>(nullable: false, defaultValue: 1),
                Status = table.Column<string>(maxLength: 32, nullable: false),
                StepHistoryJson = table.Column<string>(nullable: false, defaultValue: "[]"),
                ContextJson = table.Column<string>(nullable: true),
                InitiatedBy = table.Column<Guid>(nullable: true),
                InitiatedByName = table.Column<string>(maxLength: 200, nullable: true),
                StartedAt = table.Column<DateTime>(nullable: false),
                CompletedAt = table.Column<DateTime>(nullable: true),
                ApprovalRequestId = table.Column<Guid>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowInstances", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowInstances_EntityType_EntityId",
            table: "WorkflowInstances",
            columns: new[] { "EntityType", "EntityId" });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowInstances_Status",
            table: "WorkflowInstances",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowInstances_DefinitionId",
            table: "WorkflowInstances",
            column: "DefinitionId");

        migrationBuilder.CreateTable(
            name: "WorkflowDelegations",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TenantId = table.Column<Guid>(nullable: false),
                PrincipalId = table.Column<Guid>(nullable: false),
                PrincipalName = table.Column<string>(maxLength: 200, nullable: true),
                DelegateId = table.Column<Guid>(nullable: false),
                DelegateName = table.Column<string>(maxLength: 200, nullable: true),
                WorkflowCode = table.Column<string>(maxLength: 100, nullable: true),
                DomainEntityType = table.Column<string>(maxLength: 100, nullable: true),
                ActiveFrom = table.Column<DateTime>(nullable: false),
                ActiveUntil = table.Column<DateTime>(nullable: true),
                IsRevoked = table.Column<bool>(nullable: false, defaultValue: false),
                Reason = table.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowDelegations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowDelegations_PrincipalId_DelegateId_ActiveFrom",
            table: "WorkflowDelegations",
            columns: new[] { "PrincipalId", "DelegateId", "ActiveFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowDelegations_IsRevoked_ActiveUntil",
            table: "WorkflowDelegations",
            columns: new[] { "IsRevoked", "ActiveUntil" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WorkflowDelegations");
        migrationBuilder.DropTable(name: "WorkflowInstances");
        migrationBuilder.DropTable(name: "WorkflowDefinitions");
    }
}
