using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletesAtUtc",
                table: "city_buildings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingLevel",
                table: "city_buildings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCityCentre",
                table: "building_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "audit_ledger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MoneyDeltaCent = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfterCent = table.Column<long>(type: "bigint", nullable: false),
                    ResourceDeltas = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_ledger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "building_costs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BuildingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_building_costs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_building_costs_building_definitions_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "building_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.PlayerId, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_ledger_OccurredAtUtc",
                table: "audit_ledger",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_ledger_PlayerId_OccurredAtUtc",
                table: "audit_ledger",
                columns: new[] { "PlayerId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_building_costs_BuildingId_ResourceId",
                table: "building_costs",
                columns: new[] { "BuildingId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_CreatedAtUtc",
                table: "idempotency_keys",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_ledger");

            migrationBuilder.DropTable(
                name: "building_costs");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "CompletesAtUtc",
                table: "city_buildings");

            migrationBuilder.DropColumn(
                name: "PendingLevel",
                table: "city_buildings");

            migrationBuilder.DropColumn(
                name: "IsCityCentre",
                table: "building_definitions");
        }
    }
}
