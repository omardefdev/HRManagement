using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MissionFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Destination = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    Sponsor = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Documents = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualificationCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    TitleAr = table.Column<string>(type: "text", nullable: false),
                    TitleEn = table.Column<string>(type: "text", nullable: true),
                    RequiredCourses = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedCourses = table.Column<string>(type: "jsonb", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ContactInfo = table.Column<string>(type: "jsonb", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    TitleAr = table.Column<string>(type: "text", nullable: false),
                    TitleEn = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourses_TrainingProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "TrainingProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrainingEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FinalScore = table.Column<decimal>(type: "numeric", nullable: true),
                    CertificateUrl = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingEnrollments_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingNominations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    NominatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    NominatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingNominations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingNominations_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    EvaluationNotes = table.Column<string>(type: "text", nullable: true),
                    GradedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    GradedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingResults_TrainingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "TrainingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissionFiles_EmployeeId",
                table: "MissionFiles",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationCodes_Code",
                table: "QualificationCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourses_Code",
                table: "TrainingCourses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourses_ProviderId",
                table: "TrainingCourses",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingEnrollments_CourseId",
                table: "TrainingEnrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingNominations_CourseId",
                table: "TrainingNominations",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingNominations_EmployeeId_CourseId",
                table: "TrainingNominations",
                columns: new[] { "EmployeeId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_Year_OrgUnitId",
                table: "TrainingPlans",
                columns: new[] { "Year", "OrgUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingResults_EnrollmentId",
                table: "TrainingResults",
                column: "EnrollmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissionFiles");

            migrationBuilder.DropTable(
                name: "QualificationCodes");

            migrationBuilder.DropTable(
                name: "TrainingNominations");

            migrationBuilder.DropTable(
                name: "TrainingPlans");

            migrationBuilder.DropTable(
                name: "TrainingResults");

            migrationBuilder.DropTable(
                name: "TrainingEnrollments");

            migrationBuilder.DropTable(
                name: "TrainingCourses");

            migrationBuilder.DropTable(
                name: "TrainingProviders");
        }
    }
}
