using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-50 (§7): the principal on behalf of whom a delegate approved a movement, on the transfer's approval record and on the checklist's
    /// approval step. Two nullable columns on each table, no data change: the previous release neither reads nor writes them.
    /// </summary>
    public partial class MOV050_ApprovalOnBehalfOf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfName",
                table: "TransferApprovals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "TransferApprovals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfName",
                table: "MovementChecklists",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "MovementChecklists",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnBehalfOfName",
                table: "TransferApprovals");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "TransferApprovals");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfName",
                table: "MovementChecklists");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "MovementChecklists");
        }
    }
}
