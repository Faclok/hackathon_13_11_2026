using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AudioCacheIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SentenceAudio_FragmentId",
                table: "SentenceAudio");

            migrationBuilder.AlterColumn<string>(
                name: "ObjectKey",
                table: "SentenceAudio",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_SentenceAudio_FragmentId_ObjectKey",
                table: "SentenceAudio",
                columns: new[] { "FragmentId", "ObjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentenceAudio_ObjectKey",
                table: "SentenceAudio",
                column: "ObjectKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SentenceAudio_FragmentId_ObjectKey",
                table: "SentenceAudio");

            migrationBuilder.DropIndex(
                name: "IX_SentenceAudio_ObjectKey",
                table: "SentenceAudio");

            migrationBuilder.AlterColumn<string>(
                name: "ObjectKey",
                table: "SentenceAudio",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_SentenceAudio_FragmentId",
                table: "SentenceAudio",
                column: "FragmentId",
                unique: true);
        }
    }
}
