using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM032_AnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TenantId_AppliedAt",
                table: "RecruitmentApplications",
                columns: new[] { "TenantId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplicationEvents_TenantId_EventType_ToStatus_Oc~",
                table: "RecruitmentApplicationEvents",
                columns: new[] { "TenantId", "EventType", "ToStatus", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TenantId_AppliedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplicationEvents_TenantId_EventType_ToStatus_Oc~",
                table: "RecruitmentApplicationEvents");
        }
    }
}
