using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmsApi.Migrations
{
    /// <inheritdoc />
    public partial class TeacherWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lms_teacher_classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_teacher_classes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lms_teacher_translations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceText = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    TranslationText = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    LanguagePair = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_teacher_translations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lms_class_members",
                columns: table => new
                {
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_class_members", x => new { x.ClassId, x.UserId });
                    table.ForeignKey(
                        name: "FK_lms_class_members_lms_teacher_classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "lms_teacher_classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_class_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceText = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    ReferenceTranslation = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    TranslationStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TranslationRating = table.Column<int>(type: "integer", nullable: false),
                    CatalogCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeacherTranslationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_class_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lms_class_tasks_lms_teacher_classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "lms_teacher_classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_task_submissions",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lms_task_submissions", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_lms_task_submissions_lms_class_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "lms_class_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lms_class_members_UserId",
                table: "lms_class_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_lms_class_tasks_ClassId",
                table: "lms_class_tasks",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_lms_teacher_classes_TeacherId",
                table: "lms_teacher_classes",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_lms_teacher_translations_TeacherId",
                table: "lms_teacher_translations",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lms_class_members");

            migrationBuilder.DropTable(
                name: "lms_task_submissions");

            migrationBuilder.DropTable(
                name: "lms_teacher_translations");

            migrationBuilder.DropTable(
                name: "lms_class_tasks");

            migrationBuilder.DropTable(
                name: "lms_teacher_classes");
        }
    }
}
