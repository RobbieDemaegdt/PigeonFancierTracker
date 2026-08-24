using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherToFlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeatherBeaufort",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WeatherCapturedAtUtc",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherCondition",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeatherDay",
                table: "Flights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeatherHumidity",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeatherTemperature",
                table: "Flights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeatherWind",
                table: "Flights",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeatherBeaufort",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherCapturedAtUtc",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherCondition",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherDay",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherHumidity",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherTemperature",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "WeatherWind",
                table: "Flights");
        }
    }
}
