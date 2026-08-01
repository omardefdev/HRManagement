using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY_EOS001_UniqueSeparationRecordId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EndOfServiceSettlements_SeparationRecordId",
                table: "EndOfServiceSettlements",
                column: "SeparationRecordId",
                unique: true,
                filter: "\"SeparationRecordId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EndOfServiceSettlements_SeparationRecordId",
                table: "EndOfServiceSettlements");
        }
    }
}
