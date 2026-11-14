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
        var levelCoins = 0;
        var levelXp = 0;
        for (var number = 1; number <= 5; number++)
        {
            var level = await CurrentLevel(student.Client);
            levelCoins += level.QuestionCount / 3;
            levelXp += level.QuestionCount * 5;
            Assert.Equal(number, level.Number);
            for (var i = 0; i < level.QuestionCount; i++)
            {
                var question = await CurrentLevel(student.Client);
                Assert.True((await AnswerLevel(student.Client, question, new TextExerciseAnswer("Hello"))).Recognized);
            }
        }
        Assert.True((await student.Client.GetFromJsonAsync<BossAccessResponse>("/api/boss/access"))!.CanAccess);
        Assert.Equal(1, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Swords);
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
        Assert.Equal(levelXp + 120, profile.Xp);
        Assert.Equal(levelCoins + 3, profile.Coins);
        Assert.Equal(0, profile.Swords);
        Assert.Contains("boss", profile.TrophyIds);
        using (var expert = await ResolutionFlowTests.Staff(f, admin, "expert"))
        {
            var selectedCase = caseIds.First();
            var view = (await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{selectedCase}"))!;
            var selected = Assert.Single(view.Variants).Id;
            await Post<ResolutionResponse>(expert, $"/api/expert/cases/{selectedCase}/resolution",
                new ResolveCaseRequest(0, selected, [], []));
            await ResolutionFlowTests.Process(f, selectedCase);
            Assert.Equal(levelCoins + 7, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Coins);
            Assert.Equal(levelXp + 140, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Xp);
            await ResolutionFlowTests.Process(f, selectedCase);
            Assert.Equal(levelCoins + 7, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Coins);
            Assert.Equal(levelXp + 140, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Xp);
            var receipt = await Post<PlayerLevelRewardReceipt>(student.Client, "/api/player/level-rewards/claim", new { });
            var notice = Assert.Single(receipt.Notices);
            Assert.Equal(4, notice.Coins);
            Assert.Equal(20, notice.Xp);
            var replay = await Post<PlayerLevelRewardReceipt>(student.Client, "/api/player/level-rewards/claim", new { });
            Assert.Equal(notice.Id, Assert.Single(replay.Notices).Id);
            var outsider = await f.Student(admin, "notice-outsider");
            (await outsider.Client.PostAsJsonAsync("/api/player/rewards/acknowledge", new[] { notice.Id })).EnsureSuccessStatusCode();
            Assert.Single((await Post<PlayerLevelRewardReceipt>(student.Client, "/api/player/level-rewards/claim", new { })).Notices);
            (await student.Client.PostAsJsonAsync("/api/player/rewards/acknowledge", new[] { notice.Id })).EnsureSuccessStatusCode();
            Assert.Empty((await Post<PlayerLevelRewardReceipt>(student.Client, "/api/player/level-rewards/claim", new { })).Notices);
            // Dismissing a notice must not erase the deduplication history.
            await ResolutionFlowTests.Process(f, selectedCase);
            Assert.Equal(levelXp + 140, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Xp);
            Assert.Empty((await Post<PlayerLevelRewardReceipt>(student.Client, "/api/player/level-rewards/claim", new { })).Notices);
        }
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
        var sql = db.GetService<IMigrator>().GenerateScript("20261114201808_RetireDailyLearning", "20261114203704_MergePlayerEconomyAndLevels");
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("CREATE TABLE", sql);
        Assert.NotNull(migration.TargetModel.FindEntityType(typeof(PlayerRewardNotice).FullName!));
        Assert.NotNull(migration.TargetModel.FindEntityType(typeof(LevelAnswer).FullName!)!.FindProperty("XpAwarded"));
        var rewardSql = db.GetService<IMigrator>().GenerateScript("20261115120000_PlayerLevelRewards");
        Assert.Contains("CREATE TABLE \"PlayerRewardNotice\"", rewardSql);
        Assert.Contains("ADD \"XpAwarded\"", rewardSql);
        Assert.DoesNotContain("DROP TABLE", rewardSql);
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
