using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Identity;
using BashkirSchool.Domain;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BASHKIR_TEST_POSTGRES")))
            Skip = "Set BASHKIR_TEST_POSTGRES to a PostgreSQL test server connection with CREATE DATABASE permission.";
    }
}

public sealed class PostgresStartupTests
{
    [PostgresFact]
    public async Task ParallelCasesAndRepeatedProcessingPreserveOneParticipantsTrustTotals()
    {
        await using var database = await TestDatabase.Create();
        using var factory = new PostgresFactory(database.ConnectionString, "admin", Password, background: false);
        using var client = factory.CreateClient();
        Guid participantId; var caseIds = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var admin = await db.Users.SingleAsync();
            var participant = new Account { Id = Guid.NewGuid(), UserName = "parallel-student", NormalizedUserName = "PARALLEL-STUDENT" };
            participantId = participant.Id; db.Users.Add(participant); db.Participants.Add(new() { Id = participant.Id });
            var school = new School { Name = "School" }; var group = new StudyClass { SchoolId = school.Id, TeacherId = admin.Id, Name = "Class" };
            var policy = new DistributionPolicy { Version = 2, RequiredParticipants = 1, MinimumSchools = 1, MinimumClasses = 1, MaximumSchoolShare = 1, MaximumClassShare = 1, ChangedBy = admin.Id, CreatedAt = DateTimeOffset.UtcNow };
            db.AddRange(school, group, policy);
            for (var i = 0; i < 2; i++)
            {
                var now = DateTimeOffset.UtcNow;
                var fragment = Fragment.Create(admin.Id, "parallel-" + i, "Һаумы", "ba-ru", "{}");
                var c = TranslationCase.Open(fragment.Id, Guid.NewGuid(), now);
                var variant = TranslationVariant.Create(c.Id, "Hello", true, c.ShownVariantId);
                var assignment = ReviewAssignment.Issue(participantId, c, school.Id, group.Id, policy.Id, now, TimeSpan.FromMinutes(30));
                var response = ReviewResponse.Submit(assignment, participantId, ReviewAction.Confirm, null, .5m, now, false);
                db.AddRange(fragment, c, variant, assignment, response);
                db.Judgments.AddRange(response.Judgments(variant.Id, null));
                db.Outbox.Add(new() { CaseId = c.Id, ResponseId = response.Id, CreatedAt = now });
                caseIds.Add(c.Id);
            }
            await db.SaveChangesAsync();
        }
        async Task Process(Guid caseId)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var scope = factory.Services.CreateScope();
                try { await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(caseId, default); return; }
                catch (Exception e) when (DatabaseConflicts.IsRetryable(e) && attempt < 4) { }
                await Task.Delay(20);
            }
        }
        await Task.WhenAll(caseIds.Select(Process));
        await Task.WhenAll(caseIds.Select(Process));
        using var final = factory.Services.CreateScope(); var finalDb = final.ServiceProvider.GetRequiredService<CoreDbContext>();
        var trust = await finalDb.Participants.SingleAsync(x => x.Id == participantId);
        Assert.Equal(2, trust.Correct); Assert.Equal(2, trust.Total);
        Assert.Equal(2, await finalDb.TrustAssessments.CountAsync());
        Assert.All(await finalDb.Responses.ToArrayAsync(), r => Assert.Equal(.5m, r.TrustAtSubmission));
        Assert.False(await finalDb.Outbox.AnyAsync(x => x.ProcessedAt == null));
    }

    [PostgresFact]
    public async Task UpgradePreservesExistingAdministratorAndBackfillsOwnSessionPermission()
    {
        await using var database = await TestDatabase.Create();
        Guid id;
        await using (var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261114085937_PersistDataProtectionKeys");
            var account = new Account { Id = Guid.NewGuid(), UserName = "admin", NormalizedUserName = "ADMIN", SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, Password);
            // Seed the historical schema explicitly; the current Account model now has additional columns.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AspNetUsers" ("Id", "Active", "SessionVersion", "UserName", "NormalizedUserName", "EmailConfirmed",
                    "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ({account.Id}, TRUE, 0, {account.UserName}, {account.NormalizedUserName}, FALSE,
                    {account.PasswordHash}, {account.SecurityStamp}, {account.ConcurrencyStamp}, FALSE, FALSE, FALSE, 0)
                """);
            db.Access.Add(new() { SubjectId = account.Id, ProfileId = "administrator", Scope = AccessScope.Global });
            await db.SaveChangesAsync(); id = account.Id;
        }
        using var factory = new PostgresFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await Login(client, "admin");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sessions")).StatusCode);
        using var scope = factory.Services.CreateScope(); var upgraded = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(id, (await upgraded.Users.SingleAsync()).Id);
        Assert.Equal(1, await upgraded.Access.CountAsync(x => x.SubjectId == id && x.ProfileId == "account"));
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
    }

    [PostgresFact]
    public async Task IndependentParticipantUsesNullGroupsAndAdministratorRecoveryOnRealPostgres()
    {
        await using var database = await TestDatabase.Create();
        using var f = new PostgresFactory(database.ConnectionString, "admin", Password, background: false);
        using var admin = f.CreateClient(); await Login(admin, "admin");
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(2, .8m, 1, 1, 1, 1));
        var producerAccount = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("producer", "producer"));
        await Activate(admin, producerAccount.Id);
        using var producer = f.CreateClient(); await Login(producer, "producer"); var delivery = await Deliver(producer);
        Guid firstId = default;
        for (var i = 0; i < 2; i++)
        {
            using var person = f.CreateClient();
            var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("standalone" + i, Password));
            if (i == 0) firstId = account.Id;
            await Login(person, account.Username);
            var assignment = await Post<AssignmentResponse>(person, "/api/reviews/next", new { });
            await Post<ReviewReceipt>(person, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        }
        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(delivery.CaseId, default);
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.All(await db.Assignments.AsNoTracking().ToArrayAsync(), a => { Assert.Null(a.SchoolId); Assert.Null(a.ClassId); });
            Assert.False(await db.Enrollments.AnyAsync()); Assert.False(await db.Schools.AnyAsync());
            var translationCase = await db.Cases.AsNoTracking().SingleAsync(); Assert.True(translationCase.NeedsExpert); Assert.Null(translationCase.ResolutionId);
            Assert.All(await db.Participants.ToArrayAsync(), p => Assert.Equal(0, p.Total));
        }
        var setup = await Post<PasswordSetupResponse>(admin, $"/api/accounts/{firstId}/recovery-link", new RecoverIndependentAccountRequest("Manual identity verification reference."));
        using var recovered = f.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await recovered.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(firstId, setup.Token, "Recovered.Password123!"))).StatusCode);
        await Login(recovered, "standalone0", "Recovered.Password123!");
        var profile = await recovered.GetFromJsonAsync<MyProfileResponse>("/api/me");
        Assert.Equal(firstId, profile!.Id); Assert.Null(profile.SchoolId); Assert.Null(profile.ClassId);
    }

    [PostgresFact]
    public async Task BackgroundWorkerProcessesTwoCasesAndTrustContributionsAreNotDuplicated()
    {
        await using var database = await TestDatabase.Create();
        using var f = new PostgresFactory(database.ConnectionString, "admin", Password);
        using var admin = f.CreateClient(); await Login(admin, "admin");
        var school = await Post<IdResponse>(admin, "/api/schools", new CreateSchoolRequest("School"));
        var teacherAccount = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("teacher", "teacher", school.Id));
        await Activate(admin, teacherAccount.Id);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher");
        var group = await Post<IdResponse>(teacher, "/api/classes", new CreateClassRequest(school.Id, "Class"));
        var studentAccount = await Post<AccountResponse>(teacher, $"/api/classes/{group.Id}/students", new CreateStudentRequest("student"));
        await Activate(teacher, studentAccount.Id);
        using var student = f.CreateClient(); await Login(student, "student");
        var producerAccount = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("producer", "producer"));
        await Activate(admin, producerAccount.Id);
        using var producer = f.CreateClient(); await Login(producer, "producer");
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(1, .8m, 1, 1, 1, 1));
        var first = await Deliver(producer, "first"); var second = await Deliver(producer, "second");
        for (var i = 0; i < 2; i++)
        {
            var assignment = await Post<AssignmentResponse>(student, "/api/reviews/next", new { });
            await Post<ReviewReceipt>(student, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            if (await db.TrustAssessments.CountAsync(timeout.Token) == 2) break;
            await Task.Delay(100, timeout.Token);
        }
        foreach (var caseId in new[] { second.CaseId, first.CaseId, first.CaseId })
        {
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(caseId, default);
        }
        using var finalScope = f.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var participant = await finalDb.Participants.SingleAsync();
        Assert.Equal(2, participant.Correct); Assert.Equal(2, participant.Total);
        Assert.Equal(2, await finalDb.TrustAssessments.CountAsync());
        Assert.False(await finalDb.Cases.AnyAsync(x => x.ExpertClosed));
        Assert.False(await finalDb.Outbox.AnyAsync(x => x.ProcessedAt == null));
    }

    [PostgresFact]
    public async Task EmptyDatabaseMigratesBootstrapsAndKeepsPasswordTokenAcrossRestart()
    {
        await using var database = await TestDatabase.Create();
        PasswordSetupResponse setup;
        Guid originalAdmin;
        using (var first = new PostgresFactory(database.ConnectionString, "admin", Password))
        {
            using var admin = first.CreateClient(); // Normal startup, no migrate/bootstrap command.
            await Login(admin, "admin");
            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.True(await db.Policies.AnyAsync());
            originalAdmin = (await db.Users.SingleAsync()).Id;
            var expert = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("expert", "expert"));
            setup = await Post<PasswordSetupResponse>(admin, $"/api/accounts/{expert.Id}/password-link", new { });
            Assert.True(await db.DataProtectionKeys.AnyAsync());
        }
        using (var second = new PostgresFactory(database.ConnectionString, "ignored-admin", "Ignored.New.Password123!"))
        {
            using var client = second.CreateClient();
            var completed = await client.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(setup.AccountId, setup.Token, Password));
            Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
            await Login(client, "admin");
            using var scope = second.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.Equal(originalAdmin, (await db.Users.SingleAsync(x => x.UserName == "admin")).Id);
            Assert.False(await db.Users.AnyAsync(x => x.UserName == "ignored-admin"));
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        using (var third = new PostgresFactory(database.ConnectionString))
        {
            using var client = third.CreateClient(); // Existing database needs no bootstrap secrets.
            await Login(client, "admin");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }
    }

    [PostgresFact]
    public async Task EmptyDatabaseWithoutBootstrapSecretsRefusesStartupWithoutCreatingAccounts()
    {
        await using var database = await TestDatabase.Create();
        using var factory = new PostgresFactory(database.ConnectionString);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Bootstrap:Username", error.Message);
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(await db.Users.AnyAsync());
    }
}

internal sealed class PostgresFactory(string connection, string? username = null, string? password = null, bool background = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Core", connection);
        builder.UseSetting("Jwt:Issuer", "startup-tests");
        builder.UseSetting("Jwt:Audience", "startup-tests");
        builder.UseSetting("Jwt:SigningKey", "postgres-startup-test-only-signing-key-0123456789");
        builder.UseSetting("Bootstrap:Username", username ?? "");
        builder.UseSetting("Bootstrap:Password", password ?? "");
        builder.UseSetting("DataProtection:KeyDirectory", "");
        builder.UseSetting("Processing:BackgroundEnabled", background.ToString());
    }
}

internal sealed class TestDatabase(string adminConnection, string name, string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;
    public static async Task<TestDatabase> Create()
    {
        var source = Environment.GetEnvironmentVariable("BASHKIR_TEST_POSTGRES")!;
        var name = "school_startup_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(source);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new(source, name, new NpgsqlConnectionStringBuilder(source) { Database = name }.ConnectionString);
    }
    public async ValueTask DisposeAsync()
    {
        // Only this test's freshly generated database is removed; never the configured source database.
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
