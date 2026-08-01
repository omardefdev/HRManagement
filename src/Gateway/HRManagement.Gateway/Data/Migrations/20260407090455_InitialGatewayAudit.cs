using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialGatewayAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gateway_audit_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserSub = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    QueryString = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ElapsedMs = table.Column<long>(type: "bigint", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gateway_audit_entries_CorrelationId",
                table: "gateway_audit_entries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_gateway_audit_entries_OccurredAtUtc",
                table: "gateway_audit_entries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_gateway_audit_entries_StatusCode",
                table: "gateway_audit_entries",
                column: "StatusCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gateway_audit_entries");
        }
    }
}
