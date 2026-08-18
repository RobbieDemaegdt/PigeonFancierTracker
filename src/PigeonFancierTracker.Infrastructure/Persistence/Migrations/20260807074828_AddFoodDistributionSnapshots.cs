using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodDistributionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoodDistributionSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SelectedFancierId = table.Column<int>(type: "INTEGER", nullable: false),
                    Barley = table.Column<int>(type: "INTEGER", nullable: false),
                    Grain = table.Column<int>(type: "INTEGER", nullable: false),
                    Corn = table.Column<int>(type: "INTEGER", nullable: false),
                    Peanut = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodDistributionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FoodDistributionSnapshots_SelectedFancierId_CapturedAtUtc",
                table: "FoodDistributionSnapshots",
                columns: new[] { "SelectedFancierId", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoodDistributionSnapshots");
        }
    }
}
