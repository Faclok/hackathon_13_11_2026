using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=bashperevod;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Main"))
            .Options;
        return new AppDbContext(options);
    }
}
