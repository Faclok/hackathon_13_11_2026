using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndependentParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "Audit",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SchoolId",
                table: "Assignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ClassId",
                table: "Assignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "SelfRegistered",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Permission", "ProfileId" },
                values: new object[] { "accounts.credentials.reset", "administrator" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Null group snapshots cannot be downgraded to required school IDs without an explicit data migration.
            // Let PostgreSQL reject such a downgrade rather than replacing history with fictitious IDs.
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumns: new[] { "Permission", "ProfileId" },
                keyValues: new object[] { "accounts.credentials.reset", "administrator" });

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "SelfRegistered",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<Guid>(
                name: "SchoolId",
                table: "Assignments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ClassId",
                table: "Assignments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
