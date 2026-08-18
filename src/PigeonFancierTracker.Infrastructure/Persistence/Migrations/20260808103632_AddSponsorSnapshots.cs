using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSponsorSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SponsorSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SelectedFancierId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    SponsorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Monthly = table.Column<decimal>(type: "TEXT", nullable: false),
                    Direct = table.Column<decimal>(type: "TEXT", nullable: false),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeRemaining = table.Column<int>(type: "INTEGER", nullable: false),
                    Signed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    ContractEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CanCallSponsors = table.Column<bool>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorSnapshots_SelectedFancierId_CapturedAtUtc",
                table: "SponsorSnapshots",
                columns: new[] { "SelectedFancierId", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SponsorSnapshots");
        }
    }
}
