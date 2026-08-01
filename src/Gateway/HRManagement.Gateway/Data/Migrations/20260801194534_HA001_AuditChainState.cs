using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class HA001_AuditChainState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gateway_audit_chain_state",
                columns: table => new
                {
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_audit_chain_state", x => x.ChainKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gateway_audit_chain_state");
        }
    }
}
