using System;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;

#nullable disable

namespace Dev.Analytics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN002_EnforceTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // TEN-002: backfill before NOT NULL
            TenantRlsMigrationHelper.BackfillTenantId(migrationBuilder, "Reports", "ReadModels", "KPIs", "ETLJobs", "DataRefreshSchedules", "Dashboards", "AuditLogs", "AnalyticsEvents");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "Reports",
                            type: "uuid",
                            nullable: false,
                            defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                            oldClrType: typeof(Guid),
                            oldType: "uuid",
                            oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ReadModels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "KPIs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ETLJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DataRefreshSchedules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Dashboards",
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

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AnalyticsEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
            // TEN-002: PostgreSQL RLS
            TenantRlsMigrationHelper.EnableRls(migrationBuilder, "Reports", "ReadModels", "KPIs", "ETLJobs", "DataRefreshSchedules", "Dashboards", "AnalyticsEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            TenantRlsMigrationHelper.DisableRls(migrationBuilder, "Reports", "ReadModels", "KPIs", "ETLJobs", "DataRefreshSchedules", "Dashboards", "AnalyticsEvents");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "Reports",
                            type: "uuid",
                            nullable: true,
                            oldClrType: typeof(Guid),
                            oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ReadModels",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "KPIs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ETLJobs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "DataRefreshSchedules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Dashboards",
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

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AnalyticsEvents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
