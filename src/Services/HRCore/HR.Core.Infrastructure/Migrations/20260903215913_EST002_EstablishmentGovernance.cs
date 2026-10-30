using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EST002_EstablishmentGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EstablishmentChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecisionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JustificationNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResultEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstablishmentChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstablishmentChangeRequests_LegislationSources_LegalSourceId",
                        column: x => x.LegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstablishmentChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotBeforeJson = table.Column<string>(type: "text", nullable: true),
                    SnapshotAfterJson = table.Column<string>(type: "text", nullable: true),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecisionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstablishmentChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstablishmentChangeLogs_EstablishmentChangeRequests_ChangeR~",
                        column: x => x.ChangeRequestId,
                        principalTable: "EstablishmentChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeLogs_ChangeRequestId",
                table: "EstablishmentChangeLogs",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeLogs_TenantId_EntityType_EntityId",
                table: "EstablishmentChangeLogs",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeRequests_LegalSourceId",
                table: "EstablishmentChangeRequests",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeRequests_TargetEntityId",
                table: "EstablishmentChangeRequests",
                column: "TargetEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeRequests_TenantId_RequestNumber",
                table: "EstablishmentChangeRequests",
                columns: new[] { "TenantId", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstablishmentChangeRequests_TenantId_Status",
                table: "EstablishmentChangeRequests",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EstablishmentChangeLogs");

            migrationBuilder.DropTable(
                name: "EstablishmentChangeRequests");
        }
    }
}
