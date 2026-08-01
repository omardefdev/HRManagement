using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace File.Storage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoragePlatformRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Extension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileReferences_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileReferences_FileId_HolderKey",
                table: "FileReferences",
                columns: new[] { "FileId", "HolderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_Bucket_ObjectKey",
                table: "Files",
                columns: new[] { "Bucket", "ObjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_Status",
                table: "Files",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Files_UploadedAt",
                table: "Files",
                column: "UploadedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileReferences");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Context = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Bucket_ObjectKey",
                table: "StoredFiles",
                columns: new[] { "Bucket", "ObjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Context",
                table: "StoredFiles",
                column: "Context");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_EntityType_EntityId",
                table: "StoredFiles",
                columns: new[] { "EntityType", "EntityId" });
        }
    }
}
