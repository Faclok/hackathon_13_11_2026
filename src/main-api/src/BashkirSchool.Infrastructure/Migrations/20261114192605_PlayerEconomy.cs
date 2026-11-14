using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlayerEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCaseOpenings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DecorationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Duplicate = table.Column<bool>(type: "boolean", nullable: false),
                    Compensation = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCaseOpenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCaseOpenings_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerStates",
                columns: table => new
                {
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Coins = table.Column<int>(type: "integer", nullable: false),
                    CaseKeys = table.Column<int>(type: "integer", nullable: false),
                    BossAnswers = table.Column<int>(type: "integer", nullable: false),
                    BossRewarded = table.Column<bool>(type: "boolean", nullable: false),
                    LastDailyRewardDay = table.Column<DateOnly>(type: "date", nullable: true),
                    OwnedJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    EquippedItemsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    BackgroundId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FrameId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HatId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStates", x => x.ParticipantId);
                    table.ForeignKey(
                        name: "FK_PlayerStates_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCaseOpenings_ParticipantId_OpenedAt",
                table: "PlayerCaseOpenings",
                columns: new[] { "ParticipantId", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCaseOpenings");

            migrationBuilder.DropTable(
                name: "PlayerStates");
        }
    }
}
