using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBot.Migrations
{
    /// <inheritdoc />
    public partial class ResumeStudentAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "answer_exercise_id",
                table: "bot_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "answer_message_id",
                table: "bot_states",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "answer_exercise_id",
                table: "bot_states");

            migrationBuilder.DropColumn(
                name: "answer_message_id",
                table: "bot_states");
        }
    }
}
