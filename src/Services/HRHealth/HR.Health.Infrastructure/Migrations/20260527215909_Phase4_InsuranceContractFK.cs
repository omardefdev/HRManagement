using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Health.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_InsuranceContractFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InsuranceContractId",
                table: "InsuranceCards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceCards_InsuranceContractId",
                table: "InsuranceCards",
                column: "InsuranceContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_InsuranceCards_InsuranceContracts_InsuranceContractId",
                table: "InsuranceCards",
                column: "InsuranceContractId",
                principalTable: "InsuranceContracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InsuranceCards_InsuranceContracts_InsuranceContractId",
                table: "InsuranceCards");

            migrationBuilder.DropIndex(
                name: "IX_InsuranceCards_InsuranceContractId",
                table: "InsuranceCards");

            migrationBuilder.DropColumn(
                name: "InsuranceContractId",
                table: "InsuranceCards");
        }
    }
}
