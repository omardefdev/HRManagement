using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparationChecklistAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "SeparationChecklists",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedBy",
                table: "SeparationChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceRef",
                table: "SeparationChecklists",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "SeparationChecklists",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "SeparationChecklists",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("UPDATE \"SeparationChecklists\" SET \"IsRequired\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "EvidenceRef",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "SeparationChecklists");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "SeparationChecklists",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
