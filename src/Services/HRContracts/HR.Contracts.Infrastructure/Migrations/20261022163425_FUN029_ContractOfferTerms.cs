using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN029_ContractOfferTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentDecisionId",
                table: "ServiceTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommencementOverdueAt",
                table: "ServiceTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedStartDate",
                table: "ServiceTransactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OfferLetterId",
                table: "ServiceTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecruitmentApplicationId",
                table: "ServiceTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "ServiceTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AgreedBaseSalary",
                table: "Contracts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmploymentType",
                table: "Contracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradeCode",
                table: "Contracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalRegimeId",
                table: "Contracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceOfferLetterId",
                table: "Contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTransactions_TenantId_RecruitmentApplicationId",
                table: "ServiceTransactions",
                columns: new[] { "TenantId", "RecruitmentApplicationId" });

            // FUN-029 (E1): backfill lineage from the notes text written by ProcessCandidateAcceptedHandler
            // ("… | recruitmentApplicationId:<guid>", marker matched case-insensitively like ParseRecruitmentApplicationId).
            // Fills the new column only; Notes and every other column are untouched.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(BackfillRecruitmentApplicationIdSql);
            }
        }

        internal const string BackfillRecruitmentApplicationIdSql = """
            UPDATE "ServiceTransactions"
            SET "RecruitmentApplicationId" = (regexp_match(
                    "Notes",
                    'recruitmentApplicationId:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})',
                    'i'))[1]::uuid
            WHERE "RecruitmentApplicationId" IS NULL
              AND "Notes" IS NOT NULL
              AND "Notes" ~* 'recruitmentApplicationId:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceTransactions_TenantId_RecruitmentApplicationId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "AppointmentDecisionId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "CommencementOverdueAt",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "ExpectedStartDate",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "OfferLetterId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "RecruitmentApplicationId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "AgreedBaseSalary",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "EmploymentType",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "GradeCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LegalRegimeId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SourceOfferLetterId",
                table: "Contracts");
        }
    }
}
