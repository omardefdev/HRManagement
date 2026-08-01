using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class INT003_ProcessSagas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HireCaseSagaStates",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessCorrelationId = table.Column<string>(type: "text", nullable: true),
                    PendingParties = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CompletedParties = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastFaultParty = table.Column<string>(type: "text", nullable: true),
                    LastFaultReason = table.Column<string>(type: "text", nullable: true),
                    TimeoutTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HireCaseSagaStates", x => x.CorrelationId);
                });

            migrationBuilder.CreateTable(
                name: "ProcessInterventions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SagaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FailedParty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessInterventions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeparationSagaStates",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeparationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeparationType = table.Column<string>(type: "text", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessCorrelationId = table.Column<string>(type: "text", nullable: true),
                    PendingParties = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CompletedParties = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastFaultParty = table.Column<string>(type: "text", nullable: true),
                    LastFaultReason = table.Column<string>(type: "text", nullable: true),
                    TimeoutTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationSagaStates", x => x.CorrelationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInterventions_TenantId_SagaType_CorrelationId_Status",
                table: "ProcessInterventions",
                columns: new[] { "TenantId", "SagaType", "CorrelationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HireCaseSagaStates");

            migrationBuilder.DropTable(
                name: "ProcessInterventions");

            migrationBuilder.DropTable(
                name: "SeparationSagaStates");
        }
    }
}
