using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class MergedPlayerLevelsTests
{
    [Fact]
    public async Task PlayerBossRoutesUseFiveLevelCreditsAndRewardThreeAnswersExactlyOnce()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await LearningContent(admin);
        Assert.False((await student.Client.GetFromJsonAsync<BossAccessResponse>("/api/boss/access"))!.CanAccess);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.PostAsJsonAsync("/api/boss/next", new { })).StatusCode);
        for (var number = 1; number <= 5; number++)
        {
            var level = await CurrentLevel(student.Client);
            Assert.Equal(number, level.Number);
            for (var i = 0; i < level.QuestionCount; i++)
            {
                var question = await CurrentLevel(student.Client);
                Assert.True((await AnswerLevel(student.Client, question, new TextExerciseAnswer("Hello"))).Recognized);
            }
        }
        Assert.True((await student.Client.GetFromJsonAsync<BossAccessResponse>("/api/boss/access"))!.CanAccess);
        using var producer = await f.Producer(admin);
        for (var i = 0; i < 3; i++) await Deliver(producer, "merged-boss-" + i);
        var caseIds = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var question = await Post<AssignmentResponse>(student.Client, "/api/boss/next", new { });
            Assert.True(caseIds.Add(question.CaseId));
            var request = new SubmitReviewRequest(ReviewAction.Confirm);
            var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{question.Id}/response", request);
            Assert.Equal(receipt, await Post<ReviewReceipt>(student.Client, $"/api/reviews/{question.Id}/response", request));
        }
        var boss = await Post<LevelBossResponse>(student.Client, "/api/learning/boss", new { });
        Assert.Equal(3, boss.CompletedCount);
        Assert.Null(boss.Question);
        Assert.False((await student.Client.GetFromJsonAsync<BossAccessResponse>("/api/boss/access"))!.CanAccess);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.PostAsJsonAsync("/api/boss/next", new { })).StatusCode);
        var profile = (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!;
        Assert.Equal(100, profile.Xp);
        Assert.Equal(50, profile.Coins);
        Assert.Contains("boss", profile.TrophyIds);
        Assert.Equal(6, (await CurrentLevel(student.Client)).Number);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(3, await db.Responses.CountAsync());
        Assert.Equal(3, (await db.PlayerStates.SingleAsync(x => x.ParticipantId == student.Id)).BossAnswers);
    }

    [Fact]
    public async Task LeaderboardCountsCorrectLevelQuestionsAndIgnoresFailedAndReplayedAttempts()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await LearningContent(admin);
        var first = await CurrentLevel(student.Client);
        Assert.False((await AnswerLevel(student.Client, first, new TextExerciseAnswer("Wrong"))).Recognized);
        var next = await CurrentLevel(student.Client);
        var request = new TextExerciseAnswer("Hello");
        Assert.True((await AnswerLevel(student.Client, next, request)).Recognized);
        await AnswerLevel(student.Client, next, request);
        var profile = (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!;
        Assert.Equal(1, profile.TasksSolved);
        Assert.Equal(1, profile.Streak);
        var monthly = (await student.Client.GetFromJsonAsync<PlayerLeaderboardEntry[]>("/api/leaderboard?period=monthly&metric=tasks"))!;
        Assert.Equal(1, Assert.Single(monthly, x => x.User.Id == student.Id).Value);
        Assert.False((await student.Client.GetFromJsonAsync<BossAccessResponse>("/api/boss/access"))!.CanAccess);
    }

    [Fact]
    public void LatestMigrationIncludesEconomyAndLevelsWithoutResurrectingDailyEntities()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var migrations = db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(migrations.Migrations.Last().Value, db.Database.ProviderName!);
        Assert.NotNull(migration.TargetModel.FindEntityType(typeof(PlayerState).FullName!));
        Assert.NotNull(migration.TargetModel.FindEntityType(typeof(LearningLevel).FullName!));
        Assert.Null(migration.TargetModel.FindEntityType("BashkirSchool.Domain.DailyLesson"));
        var sql = db.GetService<IMigrator>().GenerateScript("20261114201808_RetireDailyLearning");
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("CREATE TABLE", sql);
    }

    [PostgresFact]
    public Task EconomyDeploymentUpgradesToMergedLevelsWithoutLosingBalance() => VerifyUpgrade(false);

    [PostgresFact]
    public Task LevelDeploymentAppliesEarlierEconomyMigrationWithoutLosingProgress() => VerifyUpgrade(true);

    private static async Task VerifyUpgrade(bool levelsFirst)
    {
        await using var database = await TestDatabase.Create();
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(levelsFirst ? "20261114201808_RetireDailyLearning" : "20261114192605_PlayerEconomy");
        var account = new Account { Id = Guid.NewGuid(), UserName = "merge-upgrade" };
        db.AddRange(account, new Participant { Id = account.Id });
        await db.SaveChangesAsync();
        Guid? levelId = null;
        if (levelsFirst)
        {
            // Reproduce the local branch as deployed before the economy migration existed.
            // This fixture owns a freshly generated test database; no application database is used.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"PlayerCaseOpenings\"; DROP TABLE \"PlayerStates\";");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261114192605_PlayerEconomy';");
            var level = new LearningLevel { ParticipantId = account.Id, Number = 1, StartedAt = DateTimeOffset.UtcNow };
            db.Add(level); levelId = level.Id;
        }
        else
        {
            db.Add(new PlayerState { ParticipantId = account.Id, Coins = 40, Xp = 99 });
        }
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        if (levelsFirst)
        {
            Assert.Equal(levelId, (await db.Set<LearningLevel>().SingleAsync()).Id);
            Assert.Empty(await db.PlayerStates.ToArrayAsync());
            db.Add(new PlayerState { ParticipantId = account.Id, Coins = 40 });
        }
        else
        {
            var state = await db.PlayerStates.SingleAsync();
            Assert.Equal(40, state.Coins);
            Assert.Equal(99, state.Xp);
            db.Add(new LearningLevel { ParticipantId = account.Id, Number = 1, StartedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Set<LearningLevel>().CountAsync());
        Assert.Equal(40, (await db.PlayerStates.SingleAsync()).Coins);
    }
}
