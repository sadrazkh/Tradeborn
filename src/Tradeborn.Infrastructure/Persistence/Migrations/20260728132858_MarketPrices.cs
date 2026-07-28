using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_price_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceCent = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_price_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "market_prices",
                columns: table => new
                {
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceAtLastTradeCent = table.Column<long>(type: "bigint", nullable: false),
                    LastTradeAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_prices", x => x.ResourceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_price_history_ResourceId_RecordedAtUtc",
                table: "market_price_history",
                columns: new[] { "ResourceId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_price_history");

            migrationBuilder.DropTable(
                name: "market_prices");
        }
    }
}
