using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_ApplicationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxAge",
                table: "RecruitmentRequisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinAge",
                table: "RecruitmentRequisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MinQualificationCode",
                table: "RecruitmentRequisitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinYearsOfExperience",
                table: "RecruitmentRequisitions",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "RecruitmentApplications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EducationLevelCode",
                table: "RecruitmentApplications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EligibilityEvaluatedAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EligibilityFlags",
                table: "RecruitmentApplications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EligibilityOverrideReason",
                table: "RecruitmentApplications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GraduationYear",
                table: "RecruitmentApplications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialization",
                table: "RecruitmentApplications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "YearsOfExperience",
                table: "RecruitmentApplications",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "CitizenApplications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EducationLevelCode",
                table: "CitizenApplications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GraduationYear",
                table: "CitizenApplications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialization",
                table: "CitizenApplications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "YearsOfExperience",
                table: "CitizenApplications",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxAge",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "MinAge",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "MinQualificationCode",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "MinYearsOfExperience",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "EducationLevelCode",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "EligibilityEvaluatedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "EligibilityFlags",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "EligibilityOverrideReason",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "GraduationYear",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "Specialization",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "EducationLevelCode",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "GraduationYear",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "Specialization",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "CitizenApplications");
        }
    }
}
