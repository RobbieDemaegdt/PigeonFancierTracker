using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlightResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FlightId = table.Column<int>(type: "INTEGER", nullable: false),
                    PigeonId = table.Column<int>(type: "INTEGER", nullable: false),
                    FancierId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalParticipants = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    AverageSpeed = table.Column<decimal>(type: "TEXT", nullable: false),
                    PigeonDistance = table.Column<int>(type: "INTEGER", nullable: false),
                    PigeonName = table.Column<string>(type: "TEXT", nullable: true),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Department = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PayoutType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: true),
                    LocationLat = table.Column<double>(type: "REAL", nullable: true),
                    LocationLng = table.Column<double>(type: "REAL", nullable: true),
                    DistanceKm = table.Column<int>(type: "INTEGER", nullable: false),
                    DistanceCategory = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AgeType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Subscribers = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResultsFetchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlightResults_FancierId",
                table: "FlightResults",
                column: "FancierId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightResults_FlightId_PigeonId",
                table: "FlightResults",
                columns: new[] { "FlightId", "PigeonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flights_Season_Status",
                table: "Flights",
                columns: new[] { "Season", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlightResults");

            migrationBuilder.DropTable(
                name: "Flights");
        }
    }
}
