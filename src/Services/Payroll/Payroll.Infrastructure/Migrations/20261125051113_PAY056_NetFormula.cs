using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-56 (G-PAY-03): a salary line carries the sum of its adjustments beside its gross and its deductions, so that every reader
    /// sees gross − deductions + adjustments = net.
    /// <para>
    /// <b>The fill.</b> For every existing line the column is the sum of its <c>Adjustment</c> components that reference an adjustment
    /// (<c>ReferenceId</c>). That is how the engine has always written a line's adjustments. A correction line of an older build also
    /// has a component «تصحيح رجعي — فرق إجمالي» of the same type that references nothing: it is the same difference written a second
    /// time into the base, so it is left out, or the difference would be counted twice. The fill writes no other column: no net, gross
    /// or deduction changes, and a posted line keeps the net it was posted with whatever the formula says of it.
    /// </para>
    /// <para>
    /// Both tables are tenant tables under forced row security and the migrator carries no agency, so without lifting <c>FORCE</c> on
    /// both the <c>UPDATE … FROM</c> would read no component, change no line and report success (PLAN-43's lesson; <c>WF044</c>'s and
    /// <c>PAY055</c>'s way). The owner is exempt once FORCE is off; ALTER TABLE holds an exclusive lock until the commit and rolls back
    /// with the migration, so no other session ever sees either table unforced.
    /// </para>
    /// </summary>
    public partial class PAY056_NetFormula : Migration
    {
        /// <summary>The lines' table, written by the fill under a lifted FORCE.</summary>
        internal const string LinesTable = "SalaryLines";

        /// <summary>The components' table, read by the fill under a lifted FORCE.</summary>
        internal const string ComponentsTable = "SalaryLineComponents";

        /// <summary>
        /// Each line's adjustments from its components. A line with none keeps the column's default, zero. The component type is stored
        /// as its name.
        /// </summary>
        internal const string FillTotalAdjustmentsSql = """
            UPDATE "SalaryLines" AS l
            SET "TotalAdjustments" = filled.total
            FROM (
                SELECT c."SalaryLineId" AS line_id, SUM(c."Amount") AS total
                FROM "SalaryLineComponents" AS c
                WHERE c."ComponentType" = 'Adjustment' AND c."ReferenceId" IS NOT NULL
                GROUP BY c."SalaryLineId"
            ) AS filled
            WHERE l."Id" = filled.line_id AND filled.total <> 0;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalAdjustments",
                table: "SalaryLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // The fill, as the owner with FORCE lifted on the table it reads and the table it writes (see the summary).
                migrationBuilder.Sql($"""ALTER TABLE "{ComponentsTable}" NO FORCE ROW LEVEL SECURITY;""");
                migrationBuilder.Sql($"""ALTER TABLE "{LinesTable}" NO FORCE ROW LEVEL SECURITY;""");
                migrationBuilder.Sql(FillTotalAdjustmentsSql);
                migrationBuilder.Sql($"""ALTER TABLE "{LinesTable}" FORCE ROW LEVEL SECURITY;""");
                migrationBuilder.Sql($"""ALTER TABLE "{ComponentsTable}" FORCE ROW LEVEL SECURITY;""");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalAdjustments",
                table: "SalaryLines");
        }
    }
}
