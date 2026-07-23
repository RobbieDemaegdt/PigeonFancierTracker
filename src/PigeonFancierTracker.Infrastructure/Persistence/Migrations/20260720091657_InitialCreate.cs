using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawApiSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedQuery = table.Column<string>(type: "TEXT", nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseBodyJson = table.Column<string>(type: "TEXT", nullable: false),
                    BodySha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContractVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedFancierId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceSeasonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawApiSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Profile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SelectedFancierId = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawApiSnapshots_Endpoint_NormalizedQuery_SelectedFancierId_SourceSeasonId_BodySha256",
                table: "RawApiSnapshots",
                columns: new[] { "Endpoint", "NormalizedQuery", "SelectedFancierId", "SourceSeasonId", "BodySha256" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawApiSnapshots");

            migrationBuilder.DropTable(
                name: "SyncRuns");
        }
    }
}
