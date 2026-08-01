using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddHireChecklist : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "HireChecklists",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                Step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HireChecklists", x => x.Id);
                table.ForeignKey(
                    name: "FK_HireChecklists_ServiceTransactions_ServiceTransactionId",
                    column: x => x.ServiceTransactionId,
                    principalTable: "ServiceTransactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HireChecklists_ServiceTransactionId_Step",
            table: "HireChecklists",
            columns: new[] { "ServiceTransactionId", "Step" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "HireChecklists");
    }
}
