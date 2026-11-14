using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations;

public sealed partial class PlayerLevelRewards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(
            name: "LastRewardedLevel", table: "PlayerStates", type: "integer", nullable: false, defaultValue: 1);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "LastRewardedLevel", table: "PlayerStates");
}
