using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonFancierTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncRunItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncRunItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SyncRunId = table.Column<long>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedQuery = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RawSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRunItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunItems_SyncRunId_Endpoint_NormalizedQuery",
                table: "SyncRunItems",
                columns: new[] { "SyncRunId", "Endpoint", "NormalizedQuery" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncRunItems");
        }
    }
}
