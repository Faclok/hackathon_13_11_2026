using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LearningRewardReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoinsAwarded",
                table: "LevelAnswer",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XpAwarded",
                table: "LevelAnswer",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoinsAwarded",
                table: "LevelAnswer");

            migrationBuilder.DropColumn(
                name: "XpAwarded",
                table: "LevelAnswer");
        }
    }
}
