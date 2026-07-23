using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompletedTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransferId = table.Column<int>(type: "INTEGER", nullable: false),
                    PigeonId = table.Column<int>(type: "INTEGER", nullable: true),
                    SelectedFancierId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    SoldPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Seller = table.Column<string>(type: "TEXT", nullable: true),
                    SoldTo = table.Column<string>(type: "TEXT", nullable: true),
                    PigeonName = table.Column<string>(type: "TEXT", nullable: true),
                    Sex = table.Column<string>(type: "TEXT", nullable: true),
                    Age = table.Column<string>(type: "TEXT", nullable: true),
                    BidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TransferStart = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TransferEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SkillsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletedTransfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedTransfers_SelectedFancierId_TransferId",
                table: "CompletedTransfers",
                columns: new[] { "SelectedFancierId", "TransferId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompletedTransfers");
        }
    }
}
