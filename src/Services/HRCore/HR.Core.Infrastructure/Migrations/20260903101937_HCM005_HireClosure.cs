using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM005_HireClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RecruitmentRequisitions_VacancyCount",
                table: "RecruitmentRequisitions");

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacyUnlinked",
                table: "RecruitmentApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecruitmentRequisitions_VacancyCount",
                table: "RecruitmentRequisitions",
                sql: "\"VacancyCount\" >= 0 AND \"VacancyCount\" <= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RecruitmentRequisitions_VacancyCount",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "IsLegacyUnlinked",
                table: "RecruitmentApplications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecruitmentRequisitions_VacancyCount",
                table: "RecruitmentRequisitions",
                sql: "\"VacancyCount\" = 1");
        }
    }
}
