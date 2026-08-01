using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransferFinancialImpacts_TransferRequests_TransferRequestId",
                table: "TransferFinancialImpacts");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_Tenant_Status",
                table: "TransferRequests",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_TransferFinancialImpacts_TransferRequests_TransferRequestId",
                table: "TransferFinancialImpacts",
                column: "TransferRequestId",
                principalTable: "TransferRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransferFinancialImpacts_TransferRequests_TransferRequestId",
                table: "TransferFinancialImpacts");

            migrationBuilder.DropIndex(
                name: "IX_TransferRequests_Tenant_Status",
                table: "TransferRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_TransferFinancialImpacts_TransferRequests_TransferRequestId",
                table: "TransferFinancialImpacts",
                column: "TransferRequestId",
                principalTable: "TransferRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
