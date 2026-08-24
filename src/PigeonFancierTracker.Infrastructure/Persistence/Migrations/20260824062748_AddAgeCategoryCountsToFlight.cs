using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgeCategoryCountsToFlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgeCategoryCountsCapturedAtUtc",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgeCategoryElderCount",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgeCategoryYearlingCount",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgeCategoryYouthCount",
                table: "Flights",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeCategoryCountsCapturedAtUtc",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "AgeCategoryElderCount",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "AgeCategoryYearlingCount",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "AgeCategoryYouthCount",
                table: "Flights");
        }
    }
}
