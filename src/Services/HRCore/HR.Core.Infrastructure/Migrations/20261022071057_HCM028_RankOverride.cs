using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM028_RankOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RankOverriddenAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RankOverriddenBy",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RankOverrideReason",
                table: "RecruitmentApplications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RankOverriddenAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RankOverriddenBy",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RankOverrideReason",
                table: "RecruitmentApplications");
        }
    }
}
