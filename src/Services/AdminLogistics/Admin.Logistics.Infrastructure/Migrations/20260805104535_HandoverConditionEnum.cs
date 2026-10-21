using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HandoverConditionEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HandoverCondition",
                table: "AssetAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoverConditionNotes",
                table: "AssetAssignments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnNotes",
                table: "AssetAssignments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AssetAssignments" SET "HandoverCondition" = 1
                WHERE "ConditionOnAssign" IN ('جيدة', 'جيد');

                UPDATE "AssetAssignments" SET "HandoverCondition" = 0
                WHERE "ConditionOnAssign" = 'ممتازة';

                UPDATE "AssetAssignments" SET "HandoverCondition" = 2
                WHERE "ConditionOnAssign" = 'مقبولة';

                UPDATE "AssetAssignments" SET "ReturnNotes" = "ConditionOnAssign", "HandoverCondition" = NULL
                WHERE "ConditionOnAssign" LIKE '[إعادة بسبب نقل]%';

                UPDATE "AssetAssignments" SET "HandoverConditionNotes" = "ConditionOnAssign"
                WHERE "ConditionOnAssign" IS NOT NULL
                  AND TRIM("ConditionOnAssign") <> ''
                  AND "HandoverCondition" IS NULL
                  AND "ReturnNotes" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ConditionOnAssign",
                table: "AssetAssignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionOnAssign",
                table: "AssetAssignments",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AssetAssignments" SET "ConditionOnAssign" = CASE "HandoverCondition"
                    WHEN 0 THEN 'ممتازة'
                    WHEN 1 THEN 'جيدة'
                    WHEN 2 THEN 'مقبولة'
                    ELSE COALESCE("HandoverConditionNotes", "ReturnNotes")
                END
                WHERE "HandoverCondition" IS NOT NULL
                   OR "HandoverConditionNotes" IS NOT NULL
                   OR "ReturnNotes" IS NOT NULL;

                UPDATE "AssetAssignments" SET "ConditionOnAssign" = "ReturnNotes"
                WHERE "ConditionOnAssign" IS NULL AND "ReturnNotes" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "HandoverCondition",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "HandoverConditionNotes",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "ReturnNotes",
                table: "AssetAssignments");
        }
    }
}
