using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpertPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpertRequiredParticipants",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpertSupportThreshold",
                table: "AspNetUsers",
                type: "numeric(18,12)",
                precision: 18,
                scale: 12,
                nullable: false,
                defaultValue: .8m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpertRequiredParticipants",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ExpertSupportThreshold",
                table: "AspNetUsers");
        }
    }
}
