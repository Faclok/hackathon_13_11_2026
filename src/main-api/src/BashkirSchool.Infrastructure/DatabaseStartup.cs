using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BashkirSchool.Infrastructure;

public static class DatabaseStartup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        services.GetRequiredService<IOptions<JwtSettings>>().Value.Validate();
        if (!configuration.GetValue("Database:InitializeOnStartup", true)) return;
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Core")))
            throw new InvalidOperationException("Configure ConnectionStrings:Core before starting the application.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        await db.Database.MigrateAsync(ct);
        // Hold this connection's session lock only around first-account bootstrap.
        // MigrateAsync manages its own migration lock and transactions independently.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(71830422)", ct);
            if (await db.Users.AnyAsync(ct))
            {
                var policy = await db.Policies.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                // Upgrade only the untouched bootstrap policy. Administrator-created versions remain authoritative.
                if (policy is { Version: 1, RequiredParticipants: 100, SupportThreshold: .8m,
                    MinimumSchools: 2, MinimumClasses: 3, MaximumSchoolShare: .6m,
                    MaximumClassShare: .4m, AssignmentMinutes: 30, RepeatHours: 24, WorkingSetSize: 20 })
                {
                    db.Policies.Add(new DistributionPolicy { Version = 2, ChangedBy = policy.ChangedBy,
                        CreatedAt = DateTimeOffset.UtcNow });
                    await db.SaveChangesAsync(ct);
                }
                return;
            }
            var username = configuration["Bootstrap:Username"];
            var password = configuration["Bootstrap:Password"];
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("The database has no accounts. Configure Bootstrap:Username and Bootstrap:Password for the first administrator.");
            await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync(username, password, ct);
        }
        finally
        {
            try { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(71830422)", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}

// EF tooling can generate migrations without starting the application or connecting to a database.
public sealed class DesignTimeCoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CoreDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? "Host=localhost;Database=bashkir_school;Username=postgres")
        .Options);
}
