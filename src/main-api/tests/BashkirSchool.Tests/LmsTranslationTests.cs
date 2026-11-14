using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class LmsTranslationTests
{
    [Fact]
    public async Task TeacherBossAnswerUsesOneSwordAndReceivesDelayedCorrectRewardOnce()
    {
        using var factory = new CoreFactory(lmsKey: "test-lms-secret", learningKind: "translation-text");
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        await LearningContent(admin);
        var levelCoins = 0;
        var levelXp = 0;
        for (var number = 0; number < 5; number++)
        {
            var first = await CurrentLevel(student.Client);
            levelCoins += first.QuestionCount / 3;
            levelXp += first.QuestionCount * 5;
            for (var question = 0; question < first.QuestionCount; question++)
                Assert.True((await AnswerLevel(student.Client, await CurrentLevel(student.Client), new TextExerciseAnswer("Hello"))).Recognized);
        }
        Assert.Equal(1, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Swords);
        using var teacher = await factory.Producer(admin);
        var created = await PostInternal(teacher, "/api/lms-source/teacher/translations",
            new { externalId = Guid.NewGuid(), sourceText = "Һаумы", translationText = "Привет", languagePair = "ba-ru" });
        var caseId = (await created.Content.ReadFromJsonAsync<CaseIdResponse>())!.CaseId;
        var started = await Post<BossStart>(student.Client, "/api/learning/boss/start", new { });
        Assert.Equal(5, started.Checkpoint);
        Assert.Equal(0, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Swords);
        var taskId = Guid.NewGuid();
        var contribution = await PostInternal(student.Client, $"/api/lms-source/translations/{caseId}/student-answer",
            new { taskId, text = "Привет", boss = true });
        var assignmentId = (await contribution.Content.ReadFromJsonAsync<BossContribution>())!.AssignmentId;
        var attached = await Post<LevelBossAnswerResponse>(student.Client,
            $"/api/learning/boss/teacher-answers/{assignmentId}", new { });
        Assert.False(attached.BossCompleted);
        Assert.Equal(1, attached.CoinsAwarded);
        Assert.Equal(40, attached.XpAwarded);
        Assert.Equal(attached, await Post<LevelBossAnswerResponse>(student.Client,
            $"/api/learning/boss/teacher-answers/{assignmentId}", new { }));
        Assert.Equal(levelCoins + 1, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Coins);
        Assert.Equal(levelXp + 40, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Xp);
        using var expert = await ResolutionFlowTests.Staff(factory, admin, "expert");
        var view = (await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{caseId}"))!;
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{caseId}/resolution",
            new ResolveCaseRequest(0, view.Variants.Single().Id, [], []));
        await ResolutionFlowTests.Process(factory, caseId);
        await ResolutionFlowTests.Process(factory, caseId);
        Assert.Equal(levelCoins + 5, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Coins);
        Assert.Equal(levelXp + 60, (await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Xp);
    }

    [Fact]
    public async Task StudentTaskAnswerAddsVariantAndVoteWithoutRewritingSourceOrExpertTranslation()
    {
        using var factory = new CoreFactory(lmsKey: "test-lms-secret");
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        using var teacher = await factory.Producer(admin);
        var created = await PostInternal(teacher, "/api/lms-source/teacher/translations",
            new { externalId = Guid.NewGuid(), sourceText = "Һаумы", translationText = "Привет", languagePair = "ba-ru" });
        var caseId = (await created.Content.ReadFromJsonAsync<CaseIdResponse>())!.CaseId;
        var taskId = Guid.NewGuid();
        var first = await PostInternal(student.Client, $"/api/lms-source/translations/{caseId}/student-answer",
            new { taskId, text = "Здравствуйте" });
        var again = await PostInternal(student.Client, $"/api/lms-source/translations/{caseId}/student-answer",
            new { taskId, text = "Здравствуйте" });
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal("Һаумы", (await db.Fragments.SingleAsync()).Text);
        var originalCase = await db.Cases.SingleAsync();
        Assert.Equal("Привет", (await db.Variants.SingleAsync(x => x.Id == originalCase.ShownVariantId)).Text);
        Assert.Equal(2, await db.Variants.CountAsync());
        Assert.Single(await db.Responses.ToArrayAsync());
        Assert.Equal(1, await db.Judgments.CountAsync());
        var catalog = await teacher.GetFromJsonAsync<CatalogWithTop>($"/api/lms-source/translations/{caseId}");
        Assert.Equal("Здравствуйте", catalog!.TranslationText);
        Assert.Equal("Здравствуйте", catalog.TopTranslations[0].Text);
        Assert.Equal(100, catalog.TopTranslations[0].Votes);
    }

    [Fact]
    public async Task CommunityDecisionBecomesTheAcceptedLmsAnswer()
    {
        using var factory = new CoreFactory(lmsKey: "test-lms-secret");
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        using var teacher = await factory.Producer(admin);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(1, .51m, 1, 1, 1, 1));
        var created = await PostInternal(teacher, "/api/lms-source/teacher/translations",
            new { externalId = Guid.NewGuid(), sourceText = "Һаумы", translationText = "Привет", languagePair = "ba-ru" });
        var caseId = (await created.Content.ReadFromJsonAsync<CaseIdResponse>())!.CaseId;
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(caseId, default);
        var decided = await teacher.GetFromJsonAsync<LmsCase>($"/api/lms-source/translations/{caseId}");
        Assert.Equal("community", decided!.Status);
        Assert.Equal(["Привет"], decided.AcceptedTranslations);
    }

    [Fact]
    public async Task TeacherCandidateCanBeEscalatedWithNoteAndOnlyExpertDecisionGradesIt()
    {
        using var factory = new CoreFactory(lmsKey: "test-lms-secret");
        using var admin = await factory.Bootstrap();
        using var teacher = await factory.Producer(admin);
        using var expert = await ResolutionFlowTests.Staff(factory, admin, "expert");
        var externalId = Guid.NewGuid();
        var input = new { externalId, sourceText = "Һаумы", translationText = "Привет", languagePair = "ba-ru" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await teacher.PostAsJsonAsync("/api/lms-source/teacher/translations", input)).StatusCode);
        var created = await PostInternal(teacher, "/api/lms-source/teacher/translations", input);
        var caseId = (await created.Content.ReadFromJsonAsync<CaseIdResponse>())!.CaseId;
        var pending = await teacher.GetFromJsonAsync<LmsCase>($"/api/lms-source/translations/{caseId}");
        Assert.Equal("pending", pending!.Status);
        Assert.Empty(pending.AcceptedTranslations);

        var requested = await PostInternal(teacher, $"/api/lms-source/translations/{caseId}/expert-request",
            new { note = "Проверьте окончание глагола" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Single((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        var view = await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{caseId}");
        Assert.Equal("Проверьте окончание глагола", view!.TeacherNote);
        await Post<ExpertWorkItem>(expert, $"/api/expert/cases/{caseId}/claim", new { });
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{caseId}/resolution",
            new ResolveCaseRequest(view.Revision, view.Variants[0].Id, [], []));
        var decided = await teacher.GetFromJsonAsync<LmsCase>($"/api/lms-source/translations/{caseId}");
        Assert.Equal("expert", decided!.Status);
        Assert.Equal(["Привет"], decided.AcceptedTranslations);
    }

    private static async Task<HttpResponseMessage> PostInternal(HttpClient client, string path, object value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-Internal-Key", "test-lms-secret");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private sealed record CaseIdResponse(Guid CaseId);
    private sealed record BossStart(Guid Id, long Checkpoint);
    private sealed record BossContribution(Guid AssignmentId);
    private sealed record LmsCase(string Status, string[] AcceptedTranslations);
    private sealed record CatalogWithTop(string TranslationText, PopularVariant[] TopTranslations);
    private sealed record PopularVariant(string Text, int Votes);
}
