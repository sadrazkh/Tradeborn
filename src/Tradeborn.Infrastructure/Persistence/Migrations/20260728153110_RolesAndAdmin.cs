using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RolesAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorPlayerId",
                table: "audit_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_flags", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_ledger_ActorPlayerId",
                table: "audit_ledger",
                column: "ActorPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flags");

            migrationBuilder.DropIndex(
                name: "IX_audit_ledger_ActorPlayerId",
                table: "audit_ledger");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "players");

            migrationBuilder.DropColumn(
                name: "ActorPlayerId",
                table: "audit_ledger");
        }
    }
}
