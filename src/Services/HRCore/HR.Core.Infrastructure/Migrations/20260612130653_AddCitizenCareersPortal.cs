using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenCareersPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicJobPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Entity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    ClosingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicJobPostings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CitizenApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicJobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FullName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CoverNote = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CitizenApplications_PublicJobPostings_PublicJobPostingId",
                        column: x => x.PublicJobPostingId,
                        principalTable: "PublicJobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_PublicJobPostingId",
                table: "CitizenApplications",
                column: "PublicJobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_ReferenceNumber",
                table: "CitizenApplications",
                column: "ReferenceNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitizenApplications");

            migrationBuilder.DropTable(
                name: "PublicJobPostings");
        }
    }
}
