using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlayerProgressionEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerRewardNotice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JudgmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Coins = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Seen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRewardNotice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerRewardNotice_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRewardNotice_JudgmentId_ResolutionId",
                table: "PlayerRewardNotice",
                columns: new[] { "JudgmentId", "ResolutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRewardNotice_ParticipantId_Seen",
                table: "PlayerRewardNotice",
                columns: new[] { "ParticipantId", "Seen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerRewardNotice");
        }
    }
}
