using System;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;

#nullable disable

namespace IT.Infra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN002_EnforceTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // TEN-002: backfill before NOT NULL
            TenantRlsMigrationHelper.BackfillTenantId(migrationBuilder, "UserDevices", "SystemPermissionRecords", "ServerDevices", "SecurityIncidents", "ITSupportTickets", "IntegrationMessageReceipts", "EnvironmentConfigurations", "BackupJobs", "AuditLogs");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "UserDevices",
                            type: "uuid",
                            nullable: false,
                            defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                            oldClrType: typeof(Guid),
                            oldType: "uuid",
                            oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "SystemPermissionRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ServerDevices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "SecurityIncidents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ITSupportTickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "IntegrationMessageReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "EnvironmentConfigurations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "BackupJobs",
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
            // TEN-002: PostgreSQL RLS
            TenantRlsMigrationHelper.EnableRls(migrationBuilder, "UserDevices", "SystemPermissionRecords", "ServerDevices", "SecurityIncidents", "ITSupportTickets", "EnvironmentConfigurations", "BackupJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            TenantRlsMigrationHelper.DisableRls(migrationBuilder, "UserDevices", "SystemPermissionRecords", "ServerDevices", "SecurityIncidents", "ITSupportTickets", "EnvironmentConfigurations", "BackupJobs");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "UserDevices",
                            type: "uuid",
                            nullable: true,
                            oldClrType: typeof(Guid),
                            oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "SystemPermissionRecords",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ServerDevices",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "SecurityIncidents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ITSupportTickets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "IntegrationMessageReceipts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "EnvironmentConfigurations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "BackupJobs",
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
        }
    }
}
