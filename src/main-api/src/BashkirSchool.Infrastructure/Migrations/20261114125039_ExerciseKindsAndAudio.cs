using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExerciseKindsAndAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Exercise_LessonId",
                table: "Exercise");

            migrationBuilder.AddColumn<Guid>(
                name: "SelectedVariantId",
                table: "Responses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolutionId",
                table: "Exercise",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "Exercise",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "AnswerKeyJson",
                table: "Exercise",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Exercise",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "translation-text");

            migrationBuilder.AddColumn<string>(
                name: "LanguagePair",
                table: "Exercise",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "ba-ru");

            migrationBuilder.AddColumn<string>(
                name: "PromptJson",
                table: "Exercise",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "Exercise",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Assignments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "translation-confirm");

            migrationBuilder.AddColumn<string>(
                name: "ShownVariantsJson",
                table: "Assignments",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "SentenceAudio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FragmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentenceAudio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentenceAudio_Fragments_FragmentId",
                        column: x => x.FragmentId,
                        principalTable: "Fragments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguagePair = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Word = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Translation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_LessonId_Kind_CaseId",
                table: "Exercise",
                columns: new[] { "LessonId", "Kind", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_SentenceAudio_FragmentId",
                table: "SentenceAudio",
                column: "FragmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyEntry_LanguagePair_Word_Translation",
                table: "VocabularyEntry",
                columns: new[] { "LanguagePair", "Word", "Translation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentenceAudio");

            migrationBuilder.DropTable(
                name: "VocabularyEntry");

            migrationBuilder.DropIndex(
                name: "IX_Exercise_LessonId_Kind_CaseId",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "SelectedVariantId",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "AnswerKeyJson",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "LanguagePair",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "PromptJson",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ShownVariantsJson",
                table: "Assignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolutionId",
                table: "Exercise",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "Exercise",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_LessonId",
                table: "Exercise",
                column: "LessonId");
        }
    }
}
