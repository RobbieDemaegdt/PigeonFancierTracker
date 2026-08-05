using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPedigreeCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OffspringCache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PigeonId = table.Column<int>(type: "INTEGER", nullable: false),
                    OffspringJson = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffspringCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PedigreeCache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PigeonId = table.Column<int>(type: "INTEGER", nullable: false),
                    PedigreeJson = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedigreeCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OffspringCache_PigeonId",
                table: "OffspringCache",
                column: "PigeonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PedigreeCache_PigeonId",
                table: "PedigreeCache",
                column: "PigeonId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OffspringCache");

            migrationBuilder.DropTable(
                name: "PedigreeCache");
        }
    }
}
