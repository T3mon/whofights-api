using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhoFights.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFollowSubSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserFollows_UserId_PromotionId_FighterId",
                table: "UserFollows");

            migrationBuilder.AddColumn<string>(
                name: "SubSeries",
                table: "UserFollows",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_UserId_PromotionId_SubSeries_FighterId",
                table: "UserFollows",
                columns: new[] { "UserId", "PromotionId", "SubSeries", "FighterId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserFollow_SubSeriesNeedsPromotion",
                table: "UserFollows",
                sql: "\"SubSeries\" IS NULL OR \"PromotionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserFollows_UserId_PromotionId_SubSeries_FighterId",
                table: "UserFollows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserFollow_SubSeriesNeedsPromotion",
                table: "UserFollows");

            migrationBuilder.DropColumn(
                name: "SubSeries",
                table: "UserFollows");

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_UserId_PromotionId_FighterId",
                table: "UserFollows",
                columns: new[] { "UserId", "PromotionId", "FighterId" },
                unique: true);
        }
    }
}
