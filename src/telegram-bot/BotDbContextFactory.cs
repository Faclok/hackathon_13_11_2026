using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>, IDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext() => CreateDbContext([]);
    public BotDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=bot;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Bot"))
            .Options;
        return new BotDbContext(options);
    }
}
