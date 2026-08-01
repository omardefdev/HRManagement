using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Records.Archive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentClassifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: true),
                    RetentionYears = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentClassifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: false),
                    ClassificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageLocation = table.Column<string>(type: "text", nullable: true),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Creator = table.Column<Guid>(type: "uuid", nullable: true),
                    RetentionPeriodYears = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveItems_DocumentClassifications_ClassificationId",
                        column: x => x.ClassificationId,
                        principalTable: "DocumentClassifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveIndexEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Keywords = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveIndexEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveIndexEntries_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DigitalFileRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileUrl = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Checksum = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalFileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitalFileRecords_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisposalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisposalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisposalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisposalRecords_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoanRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Borrower = table.Column<string>(type: "text", nullable: false),
                    LoanDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReturnedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanRecords_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalFileRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoxNumber = table.Column<string>(type: "text", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    Condition = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalFileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhysicalFileRecords_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveIndexEntries_ArchiveItemId",
                table: "ArchiveIndexEntries",
                column: "ArchiveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_ClassificationId",
                table: "ArchiveItems",
                column: "ClassificationId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_ReferenceNumber",
                table: "ArchiveItems",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalFileRecords_ArchiveItemId",
                table: "DigitalFileRecords",
                column: "ArchiveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DisposalRecords_ArchiveItemId",
                table: "DisposalRecords",
                column: "ArchiveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClassifications_Code",
                table: "DocumentClassifications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanRecords_ArchiveItemId",
                table: "LoanRecords",
                column: "ArchiveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFileRecords_ArchiveItemId",
                table: "PhysicalFileRecords",
                column: "ArchiveItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveIndexEntries");

            migrationBuilder.DropTable(
                name: "DigitalFileRecords");

            migrationBuilder.DropTable(
                name: "DisposalRecords");

            migrationBuilder.DropTable(
                name: "LoanRecords");

            migrationBuilder.DropTable(
                name: "PhysicalFileRecords");

            migrationBuilder.DropTable(
                name: "ArchiveItems");

            migrationBuilder.DropTable(
                name: "DocumentClassifications");
        }
    }
}
