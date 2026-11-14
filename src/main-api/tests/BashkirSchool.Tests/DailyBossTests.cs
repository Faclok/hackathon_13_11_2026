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

public sealed class DailyBossTests
{
    private const string Key = "daily-boss-test-key";

    [Fact]
    public void BossMigrationMatchesModelAndPreservesExistingLessons()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261114132547_ClassInvitations", "20261114160000_DailyLessonBoss");
        Assert.Contains("ALTER TABLE \"DailyLesson\" ADD \"BossAssignmentId\" uuid;", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    [Theory]
    [InlineData(ReviewAction.Confirm, null)]
    [InlineData(ReviewAction.Correct, "Здравствуйте!")]
    public async Task BossWaitsForPracticeResumesAcrossChannelsAndSavesRealReview(ReviewAction action, string? correction)
    {
        using var f = new CoreFactory(telegramKey: Key);
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        var other = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        var training = await Deliver(producer, "training");
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{training.CaseId}/resolution", new ResolveCaseRequest(0, training.VariantId, [], []));
        var content = await Deliver(producer, "boss");
        using var bot = f.CreateClient();
        bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { link.Code })).EnsureSuccessStatusCode();
        var lesson = (await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!;
        Assert.NotEmpty(lesson.Exercises);
        Assert.False(lesson.BossCompleted);
        Assert.Equal(HttpStatusCode.Conflict, (await student.Client.PostAsJsonAsync("/api/lessons/today/boss", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync("/internal/telegram/101/lesson/boss", new { })).StatusCode);
        foreach (var exercise in lesson.Exercises)
            await Post<AttemptResponse>(student.Client, $"/api/exercises/{exercise.Id}/answer", new SubmitExerciseAnswerRequest(ExerciseJson.Element(new TextExerciseAnswer("Һаумы"))));
        var boss = await Post<AssignmentResponse>(bot, "/internal/telegram/101/lesson/boss", new { });
        Assert.Equal(content.CaseId, boss.CaseId);
        Assert.Equal(content.VariantId, boss.VariantId);
        Assert.Equal("Hello", boss.Translation);
        Assert.Equal(boss.Id, (await Post<AssignmentResponse>(student.Client, "/api/lessons/today/boss", new { })).Id);
        var answer = new SubmitReviewRequest(action, correction);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/reviews/{boss.Id}/response", answer, Json)).StatusCode);
        var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{boss.Id}/response", answer);
        Assert.Equal(receipt.ResponseId, (await Post<ReviewReceipt>(bot, $"/internal/telegram/101/reviews/{boss.Id}/response", answer)).ResponseId);
        Assert.True((await bot.GetFromJsonAsync<LessonResponse>("/internal/telegram/101/lesson"))!.BossCompleted);
        Assert.True((await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!.BossCompleted);
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/lesson/boss", new { })).StatusCode);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var saved = Assert.Single(await db.Responses.ToArrayAsync());
        Assert.Equal(action, saved.Action);
        Assert.Equal(correction, saved.Correction);
        Assert.Equal(boss.Id, saved.AssignmentId);
        Assert.Single(await db.Judgments.ToArrayAsync());
        f.Clock.Now = f.Clock.Now.AddDays(1);
        var tomorrow = (await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!;
        Assert.NotEqual(lesson.Id, tomorrow.Id);
        Assert.False(tomorrow.BossCompleted);
    }

    [Fact]
    public async Task MissingMaterialDoesNotCompleteBossAndNewMaterialCanBeIssuedLater()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/lessons/today/boss", new { })).StatusCode);
        Assert.False((await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!.BossCompleted);
        using var producer = await f.Producer(admin);
        var content = await Deliver(producer);
        var boss = await Post<AssignmentResponse>(student.Client, "/api/lessons/today/boss", new { });
        Assert.Equal(content.CaseId, boss.CaseId);
    }

    [Fact]
    public async Task ExpiredBossIsReissuedAndSkippedBossDoesNotCompleteLesson()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        // Create the empty practice snapshot before introducing review material.
        await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today");
        using var producer = await f.Producer(admin);
        await Deliver(producer, "first");
        await Deliver(producer, "second");
        var first = await Post<AssignmentResponse>(student.Client, "/api/lessons/today/boss", new { });
        f.Clock.Now = first.ExpiresAt;
        var second = await Post<AssignmentResponse>(student.Client, "/api/lessons/today/boss", new { });
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync($"/api/reviews/{first.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm), Json)).StatusCode);
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{second.Id}/response", new SubmitReviewRequest(ReviewAction.Skip));
        Assert.False((await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!.BossCompleted);
    }
}
