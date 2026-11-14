using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations;

public partial class RetireDailyLearning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Only the runtime model is retired. Keep the historical tables and their data.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The archived tables already exist and remain usable by the older model.
    }
}
