using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

/// <summary>
/// L10N-002: Arabic normalized name columns + search index on Employees.
/// Backfill note: application SaveChanges interceptor stamps normalized values on write;
/// existing rows should be re-saved or batch-updated with ArabicTextNormalizer (RuleVersion)
/// outside this migration (SQLite EnsureCreated tests rely on OnModelCreating).
/// </summary>
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731181000_L10N002_ArabicNormalization")]
public partial class L10N002_ArabicNormalization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FirstNameNormalized",
            table: "Employees",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "MiddleNameNormalized",
            table: "Employees",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastNameNormalized",
            table: "Employees",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "FullNameNormalized",
            table: "Employees",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "NormalizationVersion",
            table: "Employees",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_Employees_TenantId_FullNameNormalized",
            table: "Employees",
            columns: new[] { "TenantId", "FullNameNormalized" });

        // Backfill: leave empty normalized defaults for legacy rows; runtime interceptor
        // and a follow-up data job should populate via ArabicTextNormalizer.Normalize.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Employees_TenantId_FullNameNormalized",
            table: "Employees");

        migrationBuilder.DropColumn(name: "FirstNameNormalized", table: "Employees");
        migrationBuilder.DropColumn(name: "MiddleNameNormalized", table: "Employees");
        migrationBuilder.DropColumn(name: "LastNameNormalized", table: "Employees");
        migrationBuilder.DropColumn(name: "FullNameNormalized", table: "Employees");
        migrationBuilder.DropColumn(name: "NormalizationVersion", table: "Employees");
    }
}
