using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainApi.Migrations
{
    /// <summary>Creates the original main API schema for users, source content, tasks, votes, and points.</summary>
    public partial class InitialMain : Migration
    {
        /// <summary>Creates tables in foreign-key dependency order.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    username = table.Column<string>(type: "text", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    lang_pair = table.Column<string>(type: "text", nullable: false, defaultValue: "ba-ru"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    dataset = table.Column<string>(type: "text", nullable: false),
                    original_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    attribution = table.Column<string>(type: "text", nullable: false),
                    license = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.token_hash);
                    table.ForeignKey(
                        name: "FK_sessions_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fragments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text_ba = table.Column<string>(type: "text", nullable: false),
                    quality_status = table.Column<string>(type: "text", nullable: false, defaultValue: "demo")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fragments", x => x.id);
                    table.ForeignKey(
                        name: "FK_fragments_source_items_source_id",
                        column: x => x.source_id,
                        principalTable: "source_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consensus_decisions",
                columns: table => new
                {
                    fragment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lang_pair = table.Column<string>(type: "text", nullable: false),
                    translation = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    confirmations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consensus_decisions", x => new { x.fragment_id, x.lang_pair });
                    table.ForeignKey(
                        name: "FK_consensus_decisions_fragments_fragment_id",
                        column: x => x.fragment_id,
                        principalTable: "fragments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fragment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lang_pair = table.Column<string>(type: "text", nullable: false, defaultValue: "ba-ru"),
                    text = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false, defaultValue: "demo")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_candidates", x => x.id);
                    table.ForeignKey(
                        name: "FK_translation_candidates_fragments_fragment_id",
                        column: x => x.fragment_id,
                        principalTable: "fragments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fragment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lang_pair = table.Column<string>(type: "text", nullable: false),
                    task_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_tasks_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_daily_tasks_fragments_fragment_id",
                        column: x => x.fragment_id,
                        principalTable: "fragments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_daily_tasks_translation_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "translation_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fragment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lang_pair = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    answer_text = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_submissions_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_submissions_daily_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "daily_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_submissions_fragments_fragment_id",
                        column: x => x.fragment_id,
                        principalTable: "fragments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "point_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_point_events_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_point_events_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_telegram_id",
                table: "app_users",
                column: "telegram_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_users_username",
                table: "app_users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_tasks_candidate_id",
                table: "daily_tasks",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_tasks_fragment_id",
                table: "daily_tasks",
                column: "fragment_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_tasks_user_id_lang_pair_task_date",
                table: "daily_tasks",
                columns: new[] { "user_id", "lang_pair", "task_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fragments_source_id",
                table: "fragments",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_point_events_submission_id",
                table: "point_events",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_point_events_user_id",
                table: "point_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_user_id",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_source_items_dataset_original_id",
                table: "source_items",
                columns: new[] { "dataset", "original_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_fragment_id_lang_pair",
                table: "submissions",
                columns: new[] { "fragment_id", "lang_pair" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_task_id",
                table: "submissions",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_user_id",
                table: "submissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_translation_candidates_fragment_id_lang_pair",
                table: "translation_candidates",
                columns: new[] { "fragment_id", "lang_pair" },
                unique: true);
        }

        /// <summary>Drops dependent tables before their referenced tables.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consensus_decisions");

            migrationBuilder.DropTable(
                name: "point_events");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "daily_tasks");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "translation_candidates");

            migrationBuilder.DropTable(
                name: "fragments");

            migrationBuilder.DropTable(
                name: "source_items");
        }
    }
}
