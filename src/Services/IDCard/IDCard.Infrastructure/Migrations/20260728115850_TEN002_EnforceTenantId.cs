using System;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN002_EnforceTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // TEN-002: backfill before NOT NULL
            TenantRlsMigrationHelper.BackfillTenantId(migrationBuilder, "OfficialSealRecords", "IssuedCards", "CardRequests", "CardPermissionSets", "CardDesigns", "CardDeliveryRecords", "AuditLogs", "AccessPermissions");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "OfficialSealRecords",
                            type: "uuid",
                            nullable: false,
                            defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                            oldClrType: typeof(Guid),
                            oldType: "uuid",
                            oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "IssuedCards",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardPermissionSets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardDesigns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardDeliveryRecords",
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
                table: "AccessPermissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-4000-8000-000000000001"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
            // TEN-002: PostgreSQL RLS
            TenantRlsMigrationHelper.EnableRls(migrationBuilder, "OfficialSealRecords", "IssuedCards", "CardRequests", "CardPermissionSets", "CardDesigns", "CardDeliveryRecords", "AccessPermissions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            TenantRlsMigrationHelper.DisableRls(migrationBuilder, "OfficialSealRecords", "IssuedCards", "CardRequests", "CardPermissionSets", "CardDesigns", "CardDeliveryRecords", "AccessPermissions");

            migrationBuilder.AlterColumn<Guid>(
                            name: "TenantId",
                            table: "OfficialSealRecords",
                            type: "uuid",
                            nullable: true,
                            oldClrType: typeof(Guid),
                            oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "IssuedCards",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardRequests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardPermissionSets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardDesigns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "CardDeliveryRecords",
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
                table: "AccessPermissions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
