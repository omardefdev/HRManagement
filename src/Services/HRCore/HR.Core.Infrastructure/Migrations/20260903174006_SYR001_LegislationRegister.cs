using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR001_LegislationRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegislationSources",
                columns: table => new
                {
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InstrumentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CertifiedTextHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RegisterVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegislationSources", x => x.LegalSourceId);
                });

            migrationBuilder.CreateTable(
                name: "LegislationInstrumentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ToLegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LinkType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegislationInstrumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegislationInstrumentLinks_LegislationSources_FromLegalSour~",
                        column: x => x.FromLegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegislationInstrumentLinks_LegislationSources_ToLegalSource~",
                        column: x => x.ToLegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegislationKeyArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ArticleNumber = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegislationKeyArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegislationKeyArticles_LegislationSources_LegalSourceId",
                        column: x => x.LegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegislationSourceAliases",
                columns: table => new
                {
                    Alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegislationSourceAliases", x => x.Alias);
                    table.ForeignKey(
                        name: "FK_LegislationSourceAliases_LegislationSources_LegalSourceId",
                        column: x => x.LegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_LegalSourceId",
                table: "RecruitmentRequisitions",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetters_LegalSourceId",
                table: "OfferLetters",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_LegislationInstrumentLinks_From_To_Type",
                table: "LegislationInstrumentLinks",
                columns: new[] { "FromLegalSourceId", "ToLegalSourceId", "LinkType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegislationInstrumentLinks_ToLegalSourceId",
                table: "LegislationInstrumentLinks",
                column: "ToLegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_LegislationKeyArticles_Source_Article",
                table: "LegislationKeyArticles",
                columns: new[] { "LegalSourceId", "ArticleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegislationSourceAliases_LegalSourceId",
                table: "LegislationSourceAliases",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_LegislationSources_Number",
                table: "LegislationSources",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_LegislationSources_Status",
                table: "LegislationSources",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_OfferLetters_LegislationSources_LegalSourceId",
                table: "OfferLetters",
                column: "LegalSourceId",
                principalTable: "LegislationSources",
                principalColumn: "LegalSourceId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentRequisitions_LegislationSources_LegalSourceId",
                table: "RecruitmentRequisitions",
                column: "LegalSourceId",
                principalTable: "LegislationSources",
                principalColumn: "LegalSourceId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OfferLetters_LegislationSources_LegalSourceId",
                table: "OfferLetters");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentRequisitions_LegislationSources_LegalSourceId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropTable(
                name: "LegislationInstrumentLinks");

            migrationBuilder.DropTable(
                name: "LegislationKeyArticles");

            migrationBuilder.DropTable(
                name: "LegislationSourceAliases");

            migrationBuilder.DropTable(
                name: "LegislationSources");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentRequisitions_LegalSourceId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropIndex(
                name: "IX_OfferLetters_LegalSourceId",
                table: "OfferLetters");
        }
    }
}
