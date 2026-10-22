using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <summary>
    /// FUN-029 (F): configurable contract-type map (no rows seeded here — production seeds nothing beyond DEFAULT) and
    /// the pending offer terms of unresolved hires. New table/column only; no data step.
    /// </summary>
    public partial class FUN029_ContractTypeRegimeMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingContractTerms",
                table: "ServiceTransactions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractTypeRegimeMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EmploymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ContractTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTypeRegimeMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractTypeRegimeMaps_ContractTypes_ContractTypeId",
                        column: x => x.ContractTypeId,
                        principalTable: "ContractTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypeRegimeMaps_ContractTypeId",
                table: "ContractTypeRegimeMaps",
                column: "ContractTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypeRegimeMaps_TenantId_LegalRegimeId_EmploymentType",
                table: "ContractTypeRegimeMaps",
                columns: new[] { "TenantId", "LegalRegimeId", "EmploymentType" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                TenantRlsMigrationHelper.EnableRls(migrationBuilder, "ContractTypeRegimeMaps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractTypeRegimeMaps");

            migrationBuilder.DropColumn(
                name: "PendingContractTerms",
                table: "ServiceTransactions");
        }
    }
}
