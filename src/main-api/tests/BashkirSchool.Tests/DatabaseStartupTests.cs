using BashkirSchool.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BashkirSchool.Tests;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task MissingConnectionStringFailsBeforeResolvingDatabase()
    {
        using var services = Services(validKey: true);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseStartup.InitializeAsync(services, Configuration()));
        Assert.Contains("ConnectionStrings:Core", error.Message);
    }

    [Fact]
    public async Task MissingJwtSecretFailsBeforeDatabaseInitialization()
    {
        using var services = Services(validKey: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseStartup.InitializeAsync(services, Configuration()));
        Assert.Contains("Jwt:SigningKey", error.Message);
    }

    [Fact]
    public async Task ExplicitlyDisabledInitializationDoesNotResolveOrMigrateDatabase()
    {
        // No DbContext registration: touching the database would throw.
        using var services = Services(validKey: true);
        await DatabaseStartup.InitializeAsync(services, Configuration(new KeyValuePair<string, string?>("Database:InitializeOnStartup", "false")));
        Assert.Null(services.GetService<CoreDbContext>());
    }

    private static ServiceProvider Services(bool validKey)
    {
        var services = new ServiceCollection();
        services.Configure<JwtSettings>(o =>
        {
            o.Issuer = "startup-tests";
            o.Audience = "startup-tests";
            o.SigningKey = validKey ? "startup-test-only-key-at-least-32-bytes" : "";
        });
        return services.BuildServiceProvider();
    }
    private static IConfiguration Configuration(params KeyValuePair<string, string?>[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
