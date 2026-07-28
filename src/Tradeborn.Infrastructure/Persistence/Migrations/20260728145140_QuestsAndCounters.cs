using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuestsAndCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeliveriesCompleted",
                table: "cities",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SalesCompleted",
                table: "cities",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "player_quests",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_quests", x => new { x.PlayerId, x.QuestId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_quests");

            migrationBuilder.DropColumn(
                name: "DeliveriesCompleted",
                table: "cities");

            migrationBuilder.DropColumn(
                name: "SalesCompleted",
                table: "cities");
        }
    }
}
