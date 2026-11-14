using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCoreWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Variants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "Variants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsExpert",
                table: "Cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolutionId",
                table: "Cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Cases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AccountSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSession_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyLesson",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLesson", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyLesson_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Resolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PrimaryVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedJson = table.Column<string>(type: "text", nullable: false),
                    RejectedJson = table.Column<string>(type: "text", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Resolutions_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Resolutions_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrustContributions",
                columns: table => new
                {
                    JudgmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Correct = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustContributions", x => x.JudgmentId);
                    table.ForeignKey(
                        name: "FK_TrustContributions_Judgments_JudgmentId",
                        column: x => x.JudgmentId,
                        principalTable: "Judgments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrustContributions_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshCredential",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshCredential", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_RefreshCredential_AccountSession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AccountSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exercise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Original = table.Column<string>(type: "text", nullable: false),
                    AcceptedTextsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercise", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exercise_DailyLesson_LessonId",
                        column: x => x.LessonId,
                        principalTable: "DailyLesson",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exercise_Resolutions_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "Resolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrustAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JudgmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrustAssessments_Judgments_JudgmentId",
                        column: x => x.JudgmentId,
                        principalTable: "Judgments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrustAssessments_Resolutions_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "Resolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    Recognized = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseAttempt_Exercise_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AccessProfile",
                column: "Id",
                value: "account");

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Permission", "ProfileId" },
                values: new object[,]
                {
                    { "classes.manage", "administrator" },
                    { "enrollments.manage", "administrator" },
                    { "exercises.submit", "participant" },
                    { "lessons.receive", "participant" },
                    { "profile.read", "account" },
                    { "sessions.manage", "account" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSession_AccountId",
                table: "AccountSession",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyLesson_ParticipantId_Date",
                table: "DailyLesson",
                columns: new[] { "ParticipantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_LessonId",
                table: "Exercise",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_ResolutionId",
                table: "Exercise",
                column: "ResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAttempt_ExerciseId",
                table: "ExerciseAttempt",
                column: "ExerciseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredential_SessionId",
                table: "RefreshCredential",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Resolutions_CaseId_Revision",
                table: "Resolutions",
                columns: new[] { "CaseId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resolutions_PolicyId",
                table: "Resolutions",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustAssessments_JudgmentId_ResolutionId",
                table: "TrustAssessments",
                columns: new[] { "JudgmentId", "ResolutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustAssessments_ResolutionId",
                table: "TrustAssessments",
                column: "ResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustContributions_ParticipantId",
                table: "TrustContributions",
                column: "ParticipantId");

            migrationBuilder.Sql("""
                INSERT INTO "Access" ("Id", "SubjectId", "ProfileId", "Scope", "ResourceId")
                SELECT gen_random_uuid(), u."Id", 'account', 0, NULL
                FROM "AspNetUsers" u
                WHERE NOT EXISTS (SELECT 1 FROM "Access" a WHERE a."SubjectId" = u."Id" AND a."ProfileId" = 'account');
                """);
            migrationBuilder.Sql("""
                UPDATE "Variants" v SET "CreatedAt" = c."CreatedAt" FROM "Cases" c WHERE c."Id" = v."CaseId";
                UPDATE "Variants" v SET "CreatedBy" = (
                    SELECT r."ParticipantId" FROM "Judgments" j JOIN "Responses" r ON r."Id" = j."ResponseId"
                    WHERE j."VariantId" = v."Id" AND j."Kind" = 0 ORDER BY r."SubmittedAt", r."Id" LIMIT 1
                ) WHERE NOT v."MachineGenerated";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"Access\" WHERE \"ProfileId\" = 'account';");
            migrationBuilder.DropTable(
                name: "ExerciseAttempt");

            migrationBuilder.DropTable(
                name: "RefreshCredential");

            migrationBuilder.DropTable(
                name: "TrustAssessments");

            migrationBuilder.DropTable(
                name: "TrustContributions");

            migrationBuilder.DropTable(
                name: "Exercise");

            migrationBuilder.DropTable(
                name: "AccountSession");

            migrationBuilder.DropTable(
                name: "DailyLesson");

            migrationBuilder.DropTable(
                name: "Resolutions");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "profile.read", "account" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "sessions.manage", "account" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "classes.manage", "administrator" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "enrollments.manage", "administrator" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "exercises.submit", "participant" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "lessons.receive", "participant" });

            migrationBuilder.DeleteData(
                table: "AccessProfile",
                keyColumn: "Id",
                keyValue: "account");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Variants");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Variants");

            migrationBuilder.DropColumn(
                name: "NeedsExpert",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ResolutionId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Cases");
        }
    }
}
