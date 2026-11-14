using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LmsApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialLms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lms_courses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_courses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lms_lessons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    course_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_lessons", x => x.id);
                    table.ForeignKey(
                        name: "FK_lms_lessons_lms_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "lms_courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_progress", x => new { x.user_id, x.lesson_id });
                    table.ForeignKey(
                        name: "FK_lms_progress_lms_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalTable: "lms_lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lms_lessons_course_id_position",
                table: "lms_lessons",
                columns: new[] { "course_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lms_progress_lesson_id",
                table: "lms_progress",
                column: "lesson_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lms_progress");

            migrationBuilder.DropTable(
                name: "lms_lessons");

            migrationBuilder.DropTable(
                name: "lms_courses");
        }
    }
}
