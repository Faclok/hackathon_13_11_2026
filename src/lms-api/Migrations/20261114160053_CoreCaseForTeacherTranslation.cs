using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LmsApi.Migrations
{
    /// <inheritdoc />
    public partial class CoreCaseForTeacherTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoreCaseId",
                table: "lms_teacher_translations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoreCaseId",
                table: "lms_teacher_translations");
        }
    }
}
