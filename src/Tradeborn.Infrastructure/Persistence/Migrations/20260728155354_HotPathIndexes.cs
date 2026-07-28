using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_players_CreatedAtUtc",
                table: "players",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_market_price_history_RecordedAtUtc",
                table: "market_price_history",
                column: "RecordedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_players_CreatedAtUtc",
                table: "players");

            migrationBuilder.DropIndex(
                name: "IX_market_price_history_RecordedAtUtc",
                table: "market_price_history");
        }
    }
}
