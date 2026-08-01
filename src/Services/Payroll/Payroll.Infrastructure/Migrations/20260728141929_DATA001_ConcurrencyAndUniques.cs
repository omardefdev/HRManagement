using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Intentionally empty: live-period uniqueness and PayrollRun xmin concurrency ship in PAY002/PAY003.
    /// PaymentBatch xmin is configured in PayrollDbContext and applied by subsequent DATA001 follow-up migrations.
    /// </remarks>
    public partial class DATA001_ConcurrencyAndUniques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
