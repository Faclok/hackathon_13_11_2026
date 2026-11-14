using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersonalLearningLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningLevel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningLevel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningLevel_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LevelBoss",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Checkpoint = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelBoss", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelBoss_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LevelQuestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    QueueOrder = table.Column<long>(type: "bigint", nullable: false),
                    AttemptToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PromptJson = table.Column<string>(type: "text", nullable: false),
                    AnswerKeyJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelQuestion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelQuestion_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LevelQuestion_LearningLevel_LevelId",
                        column: x => x.LevelId,
                        principalTable: "LearningLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LevelQuestion_Resolutions_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "Resolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LevelBossQuestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BossId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelBossQuestion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelBossQuestion_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LevelBossQuestion_LevelBoss_BossId",
                        column: x => x.BossId,
                        principalTable: "LevelBoss",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LevelBossQuestion_Responses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "Responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LevelAnswer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerJson = table.Column<string>(type: "text", nullable: false),
                    Recognized = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LevelCompleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelAnswer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelAnswer_LevelQuestion_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "LevelQuestion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningLevel_ParticipantId",
                table: "LearningLevel",
                column: "ParticipantId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LearningLevel_ParticipantId_Number",
                table: "LearningLevel",
                columns: new[] { "ParticipantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelAnswer_QuestionId",
                table: "LevelAnswer",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelBoss_ParticipantId",
                table: "LevelBoss",
                column: "ParticipantId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LevelBoss_ParticipantId_Checkpoint",
                table: "LevelBoss",
                columns: new[] { "ParticipantId", "Checkpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelBossQuestion_AssignmentId",
                table: "LevelBossQuestion",
                column: "AssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelBossQuestion_BossId_Position",
                table: "LevelBossQuestion",
                columns: new[] { "BossId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelBossQuestion_ResponseId",
                table: "LevelBossQuestion",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelQuestion_CaseId",
                table: "LevelQuestion",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelQuestion_LevelId_Position",
                table: "LevelQuestion",
                columns: new[] { "LevelId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelQuestion_ResolutionId",
                table: "LevelQuestion",
                column: "ResolutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelAnswer");

            migrationBuilder.DropTable(
                name: "LevelBossQuestion");

            migrationBuilder.DropTable(
                name: "LevelQuestion");

            migrationBuilder.DropTable(
                name: "LevelBoss");

            migrationBuilder.DropTable(
                name: "LearningLevel");
        }
    }
}
