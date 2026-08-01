using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceScopeAndWorkSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceRecords_EmployeeId_AttendanceDate",
                table: "DailyAttendanceRecords");

            migrationBuilder.AddColumn<int>(
                name: "WorkScheduleMode",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceRecords_EmployeeId_AttendanceDate",
                table: "DailyAttendanceRecords",
                columns: new[] { "EmployeeId", "AttendanceDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyAttendanceRecords_EmployeeId_AttendanceDate",
                table: "DailyAttendanceRecords");

            migrationBuilder.DropColumn(
                name: "WorkScheduleMode",
                table: "Employees");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendanceRecords_EmployeeId_AttendanceDate",
                table: "DailyAttendanceRecords",
                columns: new[] { "EmployeeId", "AttendanceDate" });
        }
    }
}
