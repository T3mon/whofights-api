using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhoFights.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankingLists",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Sport = table.Column<string>(type: "text", nullable: false),
                    List = table.Column<string>(type: "text", nullable: false),
                    Division = table.Column<string>(type: "text", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    SourcePage = table.Column<string>(type: "text", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RankingListId = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: true),
                    Belt = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    WikiLink = table.Column<string>(type: "text", nullable: true),
                    FighterId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankingEntries_Fighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RankingEntries_RankingLists_RankingListId",
                        column: x => x.RankingListId,
                        principalTable: "RankingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankingEntries_FighterId",
                table: "RankingEntries",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_RankingEntries_RankingListId",
                table: "RankingEntries",
                column: "RankingListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankingEntries");

            migrationBuilder.DropTable(
                name: "RankingLists");
        }
    }
}
