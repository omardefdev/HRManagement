using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DfdGapClosure_TrainingPlanNomination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TrainingPlanId",
                table: "TrainingNominations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingNominations_TrainingPlanId",
                table: "TrainingNominations",
                column: "TrainingPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingNominations_TrainingPlans_TrainingPlanId",
                table: "TrainingNominations",
                column: "TrainingPlanId",
                principalTable: "TrainingPlans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainingNominations_TrainingPlans_TrainingPlanId",
                table: "TrainingNominations");

            migrationBuilder.DropIndex(
                name: "IX_TrainingNominations_TrainingPlanId",
                table: "TrainingNominations");

            migrationBuilder.DropColumn(
                name: "TrainingPlanId",
                table: "TrainingNominations");
        }
    }
}
