using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradeborn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransportAndBuffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputBuffer",
                table: "city_buildings",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "transport_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromBuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    DepartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArrivesAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_jobs_cities_CityId",
                        column: x => x.CityId,
                        principalTable: "cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_jobs_CityId_ArrivesAtUtc",
                table: "transport_jobs",
                columns: new[] { "CityId", "ArrivesAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_jobs_FromBuildingId",
                table: "transport_jobs",
                column: "FromBuildingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_jobs");

            migrationBuilder.DropColumn(
                name: "OutputBuffer",
                table: "city_buildings");
        }
    }
}
