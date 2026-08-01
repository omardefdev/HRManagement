using System;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;

#nullable disable

namespace Auth.Directory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN002_EnforceTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // TEN-002: backfill before NOT NULL
            TenantRlsMigrationHelper.BackfillTenantId(migrationBuilder, "UserRoles", "Roles", "PermissionPolicies", "DirectorySyncRecords", "DirectoryEntries", "AuthUsers", "AuthTokens", "AuthAuditLogs", "AuditLogs");

            migrationBuilder.DropIndex(
                            name: "IX_AuthUsers_Email",
                            table: "AuthUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_Username",
                table: "AuthUsers");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "UserRoles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "PermissionPolicies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DirectorySyncRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DirectoryEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthUsers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthAuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_TenantId_Email",
                table: "AuthUsers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_TenantId_Username",
                table: "AuthUsers",
                columns: new[] { "TenantId", "Username" },
                unique: true);
            // TEN-002: PostgreSQL RLS
            TenantRlsMigrationHelper.EnableRls(migrationBuilder, "UserRoles", "Roles", "PermissionPolicies", "DirectorySyncRecords", "DirectoryEntries", "AuthUsers", "AuthTokens", "AuthAuditLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            TenantRlsMigrationHelper.DisableRls(migrationBuilder, "UserRoles", "Roles", "PermissionPolicies", "DirectorySyncRecords", "DirectoryEntries", "AuthUsers", "AuthTokens", "AuthAuditLogs");

            migrationBuilder.DropIndex(
                            name: "IX_AuthUsers_TenantId_Email",
                            table: "AuthUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_TenantId_Username",
                table: "AuthUsers");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "UserRoles",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "PermissionPolicies",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DirectorySyncRecords",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DirectoryEntries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthUsers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthTokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuthAuditLogs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_Email",
                table: "AuthUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_Username",
                table: "AuthUsers",
                column: "Username",
                unique: true);
        }
    }
}
