using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmsApi.Migrations
{
    /// <inheritdoc />
    public partial class TeacherReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "lms_task_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeacherComment",
                table: "lms_task_submissions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "TeacherCorrect",
                table: "lms_task_submissions",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "lms_task_submissions");

            migrationBuilder.DropColumn(
                name: "TeacherComment",
                table: "lms_task_submissions");

            migrationBuilder.DropColumn(
                name: "TeacherCorrect",
                table: "lms_task_submissions");
        }
    }
}
