using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class LmsTranslationTests
{
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
    private sealed record LmsCase(string Status, string[] AcceptedTranslations);
}
