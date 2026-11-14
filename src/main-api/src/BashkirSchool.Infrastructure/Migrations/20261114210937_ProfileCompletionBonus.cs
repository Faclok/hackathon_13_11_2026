using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProfileCompletionBonus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProfileCompletionRewarded",
                table: "PlayerStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("""
                UPDATE "PlayerStates" AS state
                SET "Coins" = state."Coins" + 50,
                    "ProfileCompletionRewarded" = TRUE
                FROM "AspNetUsers" AS account
                WHERE account."Id" = state."ParticipantId"
                  AND NULLIF(TRIM(account."FirstName"), '') IS NOT NULL
                  AND NULLIF(TRIM(account."LastName"), '') IS NOT NULL
                  AND state."ProfileCompletionRewarded" = FALSE
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileCompletionRewarded",
                table: "PlayerStates");
        }
    }
}
