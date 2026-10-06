using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhoFights.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingEntryRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Record",
                table: "RankingEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Record",
                table: "RankingEntries");
        }
    }
}
