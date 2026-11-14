using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class LevelFlowTests
{
    private const string Root = "/api/learning";
    private static AnswerLevelRequest Answer(LevelResponse level, string text) =>
        new(level.Question.AttemptToken, ExerciseJson.Element(level.Question.SchemaVersion == 2
            ? (object)ChoiceAnswer(level.Question, text) : new TextExerciseAnswer(text)));
    private static string AnswerUrl(LevelResponse level) => $"{Root}/levels/{level.Id}/questions/{level.Question.Id}/answer";
    private static Task<LevelResponse> Current(HttpClient client) => Post<LevelResponse>(client, Root + "/current", new { });
    private static Task<ExpertDatasetReceipt> Training(HttpClient admin) => Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
        new ExpertDatasetRow("learning", "Һаумы", "ba-ru", "Hello", true, new SourceReference("level-tests")));

    [Fact]
    public async Task MistakesMoveToTailUntilEveryQuestionIsCorrectAndRetriesAreIdempotent()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await Training(admin);
        var first = await Current(student.Client);
        Assert.Equal(1, first.Number);
        Assert.InRange(first.QuestionCount, 6, 10);
        Assert.Equal(0, first.CorrectCount);
        var wrong = Answer(first, "Wrong");
        var failed = await Post<LevelAnswerResponse>(student.Client, AnswerUrl(first), wrong);
        Assert.False(failed.Recognized);
        Assert.False(failed.LevelCompleted);
        Assert.Equal(0, failed.XpAwarded);
        Assert.Equal(0, failed.CoinsAwarded);
        Assert.Equal("Hello", failed.CorrectAnswer);
        Assert.Equal(failed, await Post<LevelAnswerResponse>(student.Client, AnswerUrl(first), wrong));
        Assert.Equal(HttpStatusCode.Conflict, (await student.Client.PostAsJsonAsync(AnswerUrl(first), Answer(first, "Hello"))).StatusCode);
        for (var i = 1; i < first.QuestionCount; i++)
        {
            var next = await Current(student.Client);
            Assert.Equal(first.Id, next.Id);
            Assert.NotEqual(first.Question.Id, next.Question.Id);
            var result = await Post<LevelAnswerResponse>(student.Client, AnswerUrl(next), Answer(next, "Hello"));
            Assert.True(result.Recognized);
            Assert.False(result.LevelCompleted);
        }
        var repeat = await Current(student.Client);
        Assert.Equal(first.Question.Id, repeat.Question.Id);
        Assert.NotEqual(first.Question.AttemptToken, repeat.Question.AttemptToken);
        Assert.Equal(first.QuestionCount - 1, repeat.CorrectCount);
        // The same question may be answered incorrectly repeatedly, without completing the level.
        Assert.False((await Post<LevelAnswerResponse>(student.Client, AnswerUrl(repeat), Answer(repeat, "Still wrong"))).LevelCompleted);
        repeat = await Current(student.Client);
        var done = await Post<LevelAnswerResponse>(student.Client, AnswerUrl(repeat), Answer(repeat, "Hello"));
        Assert.True(done.LevelCompleted);
        Assert.Equal(first.QuestionCount * 5, done.XpAwarded);
        Assert.Equal(first.QuestionCount / 3, done.CoinsAwarded);
        Assert.Equal(done, await Post<LevelAnswerResponse>(student.Client, AnswerUrl(repeat), Answer(repeat, "Hello")));
        var second = await Current(student.Client);
        Assert.Equal(2, second.Number);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await student.Client.PostAsJsonAsync(AnswerUrl(first),
            new AnswerLevelRequest(Guid.NewGuid(), ExerciseJson.Element(new TextExerciseAnswer("Hello"))))).StatusCode);
        var progress = (await student.Client.GetFromJsonAsync<LevelProgressResponse>(Root + "/progress"))!;
        Assert.Equal(1, progress.CompletedLevels);
        Assert.Equal(first.QuestionCount + 2, progress.Attempts);
        Assert.Equal(2, progress.Mistakes);
        var stats = (await student.Client.GetFromJsonAsync<LevelStatisticsResponse[]>(Root + "/statistics"))!;
        var completed = Assert.Single(stats, x => x.Number == 1);
        Assert.Equal(first.QuestionCount, completed.CorrectCount);
        Assert.Equal(first.QuestionCount + 2, completed.Attempts);
        Assert.Equal(2, completed.Mistakes);
        Assert.NotNull(completed.CompletedAt);
        var reward = (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!;
        Assert.Equal(first.QuestionCount / 3, reward.Coins);
        Assert.Equal(first.QuestionCount * 5, reward.Xp);
        Assert.Equal(0, reward.Swords);
    }

    [Fact]
    public async Task CurrentQuestionIsPrivateOwnedPersistentAndIndependentOfOtherPlayers()
    {
        using var f = new CoreFactory(telegramKey: "level-key", learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        var other = await f.Student(admin, "2");
        await Training(admin);
        var first = await Current(student.Client);
        var json = await (await student.Client.PostAsJsonAsync(Root + "/current", new { })).Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(json);
        Assert.False(payload.RootElement.TryGetProperty("questions", out _));
        Assert.Contains("options", json);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accepted", json, StringComparison.OrdinalIgnoreCase);
        var otherLevel = await Current(other.Client);
        Assert.Equal(1, otherLevel.Number);
        Assert.NotEqual(first.Id, otherLevel.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(AnswerUrl(first), Answer(first, "Hello"))).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var future = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Set<LevelQuestion>()
                .FirstAsync(x => x.LevelId == first.Id && x.Position == 1);
            Assert.Equal(HttpStatusCode.Conflict, (await student.Client.PostAsJsonAsync(
                $"{Root}/levels/{first.Id}/questions/{future.Id}/answer",
                new AnswerLevelRequest(future.AttemptToken, ExerciseJson.Element(new TextExerciseAnswer("Hello"))))).StatusCode);
        }
        using var bot = f.CreateClient();
        bot.DefaultRequestHeaders.Add("X-Internal-Key", "level-key");
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        (await bot.PostAsJsonAsync("/internal/telegram/301/link", new { link.Code })).EnsureSuccessStatusCode();
        var fromBot = await Post<LevelResponse>(bot, "/internal/telegram/301/learning/current", new { });
        Assert.Equal(first.Id, fromBot.Id);
        Assert.Equal(first.Question, fromBot.Question with { Prompt = first.Question.Prompt });
        var originalTime = f.Clock.Now;
        f.Clock.Now = f.Clock.Now.AddDays(2);
        Assert.Equal(first.Id, (await Post<LevelResponse>(bot, "/internal/telegram/301/learning/current", new { })).Id);
        f.Clock.Now = originalTime;
        await CompleteLevel(student.Client);
        Assert.Equal(2, (await Current(student.Client)).Number);
        Assert.Equal(otherLevel.Id, (await Current(other.Client)).Id);
        Assert.Equal(1, (await Current(other.Client)).Number);
        Assert.Equal(HttpStatusCode.NotFound, (await student.Client.GetAsync($"{Root}/levels/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.GetAsync(Root + "/statistics?limit=0")).StatusCode);
    }

    [Fact]
    public async Task EmptyAndUnresolvedCorpusCannotCreateLearningButCommunityAndExpertCan()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync(Root + "/current", new { })).StatusCode);
        using var producer = await f.Producer(admin);
        var content = await Deliver(producer);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync(Root + "/current", new { })).StatusCode);
        var active = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == content.CaseId);
            var policy = await db.Policies.FirstAsync();
            var resolution = ResolutionRevision.Create(c.Id, 1, ResolutionKind.Community, content.VariantId,
                [content.VariantId], [], policy.Id, null, "{}", null, f.Clock.Now);
            c.Apply(resolution); db.Add(resolution); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        var level = await Current(student.Client);
        Assert.Equal(1, level.Number);
        // A later loss of the community result does not invalidate an already issued snapshot.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == content.CaseId);
            var resolution = ResolutionRevision.Create(c.Id, 2, ResolutionKind.Pending, null, [], [],
                (await db.Policies.FirstAsync()).Id, null, "{}", null, f.Clock.Now);
            c.Apply(resolution); db.Add(resolution); await db.SaveChangesAsync();
        }
        Assert.Equal(active.Id, (await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { })).Id);
        await CompleteLevel(student.Client);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync(Root + "/current", new { })).StatusCode);
        await Training(admin);
        Assert.Equal(2, (await Current(student.Client)).Number);
    }

    [Fact]
    public async Task EveryFiveLevelsEarnAnOptionalBossWithThreeDistinctRealReviews()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        var other = await f.Student(admin, "2");
        await Training(admin);
        using var producer = await f.Producer(admin);
        for (var i = 0; i < 3; i++) await Deliver(producer, "boss" + i);
        for (var i = 0; i < 4; i++) await CompleteLevel(student.Client);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync(Root + "/boss", new { })).StatusCode);
        await CompleteLevel(student.Client);
        var boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Equal(5, boss.Checkpoint);
        Assert.Equal(3, boss.QuestionCount);
        Assert.Equal(0, boss.CompletedCount);
        Assert.NotNull(boss.Question);
        Assert.Equal(boss.Question.Id, (await Post<LevelBossResponse>(student.Client, Root + "/boss", new { })).Question!.Id);
        // Deferring even an active boss never blocks the next five learning levels.
        for (var i = 0; i < 5; i++) await CompleteLevel(student.Client);
        Assert.Equal(11, (await Current(student.Client)).Number);
        Assert.Equal(2, (await student.Client.GetFromJsonAsync<LevelProgressResponse>(Root + "/progress"))!.AvailableBosses);
        var cases = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
            Assert.Equal(i, boss.CompletedCount);
            Assert.NotNull(boss.Question);
            Assert.True(cases.Add(boss.Question.CaseId));
            var url = $"{Root}/bosses/{boss.Id}/questions/{boss.Question.Id}/answer";
            Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(url, new SubmitReviewRequest(ReviewAction.Confirm), Json)).StatusCode);
            var receipt = await Post<LevelBossAnswerResponse>(student.Client, url, new SubmitReviewRequest(ReviewAction.Confirm));
            Assert.Equal(i == 2, receipt.BossCompleted);
            Assert.Equal(receipt, await Post<LevelBossAnswerResponse>(student.Client, url, new SubmitReviewRequest(ReviewAction.Confirm)));
        }
        var progress = (await student.Client.GetFromJsonAsync<LevelProgressResponse>(Root + "/progress"))!;
        Assert.Equal(10, progress.CompletedLevels);
        Assert.Equal(1, progress.CompletedBosses);
        Assert.Equal(1, progress.AvailableBosses);
        var nextBoss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.NotEqual(boss.Id, nextBoss.Id);
        Assert.Equal(10, nextBoss.Checkpoint);
        Assert.Null(nextBoss.Question); // Repeat-hour protection is unchanged.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(3, await db.Responses.CountAsync());
        Assert.Equal(3, await db.Judgments.CountAsync());
        Assert.Equal(3, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task BossOffersChoicesWhenSeveralTranslationsExist()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await Training(admin);
        using var producer = await f.Producer(admin);
        var content = await Deliver(producer, "boss-choices");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            db.Variants.Add(TranslationVariant.Create(content.CaseId, "Another translation", false));
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 5; i++) await CompleteLevel(student.Client);
        var boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Equal("translation-choice", boss.Question!.Kind);
        Assert.Equal(2, boss.Question.Options.Count);
        var chosen = boss.Question.Options[0];
        var receipt = await Post<LevelBossAnswerResponse>(student.Client,
            $"{Root}/bosses/{boss.Id}/questions/{boss.Question.Id}/answer",
            new SubmitReviewRequest(ReviewAction.Confirm) { SelectedVariantId = chosen.Id });
        Assert.False(receipt.BossCompleted);
        Assert.Equal(1, (await Post<LevelBossResponse>(student.Client, Root + "/boss", new { })).CompletedCount);
    }

    [Fact]
    public async Task BossResumesWhenMaterialArrivesAndSkipsDoNotCompleteIt()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await Training(admin);
        for (var i = 0; i < 5; i++) await CompleteLevel(student.Client);
        var empty = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Null(empty.Question);
        Assert.Equal(0, empty.CompletedCount);
        await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
            new ExpertDatasetRow("untranslated", "Яңы һөйләм", "ba-ru", null, false, new SourceReference("test")));
        var boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Equal(empty.Id, boss.Id);
        Assert.Equal("translation-create", boss.Question!.Kind);
        var skipped = await Post<LevelBossAnswerResponse>(student.Client,
            $"{Root}/bosses/{boss.Id}/questions/{boss.Question.Id}/answer", new SubmitReviewRequest(ReviewAction.Skip));
        Assert.Equal(0, skipped.XpAwarded);
        Assert.Equal(0, skipped.CoinsAwarded);
        Assert.False(skipped.BossCompleted);
        var afterSkip = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Equal(0, afterSkip.CompletedCount);
        Assert.Null(afterSkip.Question);
        using var producer = await f.Producer(admin);
        await Deliver(producer, "expiring");
        boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        var expired = boss.Question!;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var assignment = await db.Assignments.SingleAsync(x => x.Id == expired.Id);
            db.Entry(assignment).Property(x => x.ExpiresAt).CurrentValue = f.Clock.Now;
            await db.SaveChangesAsync();
        }
        await Deliver(producer, "replacement");
        boss = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.NotEqual(expired.Id, boss.Question!.Id);
        Assert.Equal(0, boss.CompletedCount);
        Assert.Equal(HttpStatusCode.NotFound, (await student.Client.PostAsJsonAsync(
            $"{Root}/bosses/{boss.Id}/questions/{expired.Id}/answer", new SubmitReviewRequest(ReviewAction.Confirm), Json)).StatusCode);
    }

    [Fact]
    public async Task BossCreationAnswersReconcileAcrossChannelsAndNeverRepeatACompletedFragment()
    {
        using var f = new CoreFactory(telegramKey: "boss-level-key", learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await Training(admin);
        for (var i = 0; i < 5; i++) await CompleteLevel(student.Client);
        var fragment = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
            new ExpertDatasetRow("creation", "Һаумы", "ba-ru", null, false, new SourceReference("boss")));
        using var bot = f.CreateClient();
        bot.DefaultRequestHeaders.Add("X-Internal-Key", "boss-level-key");
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        (await bot.PostAsJsonAsync("/internal/telegram/302/link", new { link.Code })).EnsureSuccessStatusCode();
        var boss = await Post<LevelBossResponse>(bot, "/internal/telegram/302/learning/boss", new { });
        Assert.Equal(fragment.CaseId, boss.Question!.CaseId);
        Assert.Equal("translation-create", boss.Question.Kind);
        var url = $"{Root}/bosses/{boss.Id}/questions/{boss.Question.Id}/answer";
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync(url, new SubmitReviewRequest(ReviewAction.Confirm), Json)).StatusCode);
        // The review may be submitted through an existing channel before boss progress is saved.
        var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{boss.Question.Id}/response",
            new SubmitReviewRequest(ReviewAction.Correct, "Hello"));
        var resumed = await Post<LevelBossResponse>(bot, "/internal/telegram/302/learning/boss", new { });
        Assert.Equal(boss.Id, resumed.Id);
        Assert.Equal(1, resumed.CompletedCount);
        Assert.Null(resumed.Question);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var assignment = await db.Assignments.SingleAsync(x => x.Id == boss.Question.Id);
            // Simulate a sufficiently old historical issue without expiring HTTP credentials.
            db.Entry(assignment).Property(x => x.IssuedAt).CurrentValue = f.Clock.Now.AddDays(-2);
            await db.SaveChangesAsync();
            Assert.Equal(receipt.ResponseId, (await db.Set<LevelBossQuestion>().SingleAsync()).ResponseId);
        }
        // Even after the ordinary repeat interval, a boss needs three different fragments.
        resumed = await Post<LevelBossResponse>(student.Client, Root + "/boss", new { });
        Assert.Equal(1, resumed.CompletedCount);
        Assert.Null(resumed.Question);
        using var finalScope = f.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Single(await finalDb.Assignments.ToArrayAsync());
        Assert.Single(await finalDb.Responses.ToArrayAsync());
        Assert.Single(await finalDb.Outbox.ToArrayAsync());
    }

    [Fact]
    public async Task RegainedPendingStateWithAnotherLiveAssignmentResumesWithoutDuplicateIssuance()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        using var producer = await f.Producer(admin);
        var content = await Deliver(producer, "old-active");
        var first = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == content.CaseId);
            var r = ResolutionRevision.Create(c.Id, 1, ResolutionKind.Community, content.VariantId,
                [content.VariantId], [], (await db.Policies.FirstAsync()).Id, null, "{}", null, f.Clock.Now);
            c.Apply(r); db.Add(r); await db.SaveChangesAsync();
        }
        await Deliver(producer, "new-active");
        var second = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        Assert.NotEqual(first.Id, second.Id);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == content.CaseId);
            var r = ResolutionRevision.Create(c.Id, 2, ResolutionKind.Pending, null, [], [],
                (await db.Policies.FirstAsync()).Id, null, "{}", null, f.Clock.Now);
            c.Apply(r); db.Add(r); await db.SaveChangesAsync();
        }
        var resumed = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        Assert.Contains(resumed.Id, new[] { first.Id, second.Id });
        Assert.Equal(resumed.Id, (await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { })).Id);
        using var finalScope = f.Services.CreateScope();
        Assert.Equal(2, await finalScope.ServiceProvider.GetRequiredService<CoreDbContext>().Assignments.CountAsync());
    }

    [Fact]
    public async Task InvalidAnswersCannotAdvanceProgressAndLevelContractsDescribeActualRoutes()
    {
        using var f = new CoreFactory(learningKind: "translation-text");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await Training(admin);
        var first = await Current(student.Client);
        foreach (var body in new object[]
        {
            new AnswerLevelRequest(Guid.Empty, ExerciseJson.Element(new TextExerciseAnswer("Hello"))),
            new AnswerLevelRequest(first.Question.AttemptToken, ExerciseJson.Element("Hello")),
            new AnswerLevelRequest(first.Question.AttemptToken, ExerciseJson.Element(new { text = "Hello", recognized = true }))
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync(AnswerUrl(first), body)).StatusCode);
        Assert.Equal(first.Question.AttemptToken, (await Current(student.Client)).Question.AttemptToken);
        Assert.Equal(0, (await student.Client.GetFromJsonAsync<LevelProgressResponse>(Root + "/progress"))!.Attempts);
        using var document = JsonDocument.Parse(await student.Client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty(Root + "/current").GetProperty("post");
        Assert.Equal(1, operation.GetProperty("security").GetArrayLength());
        Assert.Equal("#/components/schemas/LevelResponse", operation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.False(schemas.GetProperty("LevelQuestionResponse").GetProperty("properties").TryGetProperty("answerKeyJson", out _));
    }

    [Fact]
    public void LevelMigrationMatchesModelAndKeepsLegacyLearningData()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261114174833_UntranslatedReviewCases");
        Assert.Contains("CREATE TABLE \"LearningLevel\"", sql);
        Assert.Contains("WHERE \"CompletedAt\" IS NULL", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
    }

    private static async Task CompleteLevel(HttpClient client)
    {
        var initial = await Current(client);
        for (var i = initial.CorrectCount; i < initial.QuestionCount; i++)
        {
            var current = await Current(client);
            Assert.Equal(initial.Id, current.Id);
            var result = await Post<LevelAnswerResponse>(client, AnswerUrl(current), Answer(current, "Hello"));
            Assert.True(result.Recognized);
            Assert.Equal(i == initial.QuestionCount - 1, result.LevelCompleted);
        }
    }
}
