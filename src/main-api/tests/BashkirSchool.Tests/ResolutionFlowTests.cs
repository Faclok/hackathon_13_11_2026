using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ResolutionFlowTests
{
    [Fact]
    public async Task RejectingAAndProposingWrongBProducesOneErrorWithoutRejectionBonus()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "B"));
        using var expert = await Staff(f, admin, "expert");
        var view = await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{d.CaseId}", Json);
        var b = view!.Variants.Single(x => x.Text == "B").Id;
        var c = await Post<IdResponse>(expert, $"/api/expert/cases/{d.CaseId}/variants", new ProposeTranslationRequest("C", 0));
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(0, c.Id, [], [d.VariantId, b]));
        await Process(f, d.CaseId);
        Assert.Equal((0, 1), await Trust(f, student.Id));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Single(await db.TrustAssessments.ToArrayAsync()); Assert.Single(await db.Judgments.ToArrayAsync());
    }
    [Fact]
    public async Task ExpertSynonymIsCorrectReprocessingIsIdempotentAndLateFeedbackIsArchived()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var first = await f.Student(admin); var second = await f.Student(admin, "2"); var late = await f.Student(admin, "3");
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        var a1 = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{a1.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        var a2 = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(second.Client, $"/api/reviews/{a2.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "B"));
        var lateAssignment = await Post<AssignmentResponse>(late.Client, "/api/reviews/next", new { });
        using var expert = await Staff(f, admin, "expert");
        var view = await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{d.CaseId}", Json);
        var b = view!.Variants.Single(x => x.Text == "B").Id;
        var resolution = await Post<ResolutionResponse>(expert, $"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(0, b, [d.VariantId], []));
        await Process(f, d.CaseId); await Process(f, d.CaseId);
        Assert.Equal(ResolutionKind.Expert, resolution.Kind);
        Assert.Equal((1, 1), await Trust(f, first.Id)); Assert.Equal((1, 1), await Trust(f, second.Id));
        var stale = await expert.PostAsJsonAsync($"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(0, d.VariantId, [], []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var archived = await Post<ReviewReceipt>(late.Client, $"/api/reviews/{lateAssignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "C"));
        Assert.True(archived.Archived);
        await Process(f, d.CaseId);
        Assert.Equal((0, 0), await Trust(f, late.Id));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(2, await db.TrustAssessments.CountAsync());
        Assert.Equal(1, (await db.Cases.SingleAsync()).Revision);
        Assert.True((await db.Cases.SingleAsync()).ExpertClosed);
        var level = await CurrentLevel(first.Client);
        var attempt = await AnswerLevel(first.Client, level, new TextExerciseAnswer("Hello"));
        Assert.True(attempt.Recognized); // A remains accepted even though B is primary.
        Assert.Equal((1, 1), await Trust(f, first.Id));
    }

    [Fact]
    public async Task PendingRetainsTrustUntilExpertReplacesOldContributionAndCorrectionHasOnlyOneClaim()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var first = await f.Student(admin); var second = await f.Student(admin, "2");
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(1, .8m, 1, 1, 1, 1));
        var a1 = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        // New reviews stop after a community result; an already issued review may still arrive late.
        var a2 = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{a1.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        await Process(f, d.CaseId);
        Assert.Equal((1, 1), await Trust(f, first.Id));
        await Post<ReviewReceipt>(second.Client, $"/api/reviews/{a2.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "B"));
        await Process(f, d.CaseId);
        Assert.Equal((1, 1), await Trust(f, first.Id));
        using var expert = await Staff(f, admin, "expert");
        var view = await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{d.CaseId}", Json);
        Assert.Equal(ResolutionKind.Pending, view!.Resolution!.Kind); Assert.True(view.Disputed); Assert.False(view.Closed);
        var b = view.Variants.Single(x => x.Text == "B").Id;
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(view.Revision, b, [], [d.VariantId]));
        await Process(f, d.CaseId); await Process(f, d.CaseId);
        Assert.Equal((0, 1), await Trust(f, first.Id)); Assert.Equal((1, 1), await Trust(f, second.Id));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(2, await db.Judgments.CountAsync());
        Assert.All(await db.Judgments.ToArrayAsync(), x => Assert.Equal(JudgmentKind.Support, x.Kind));
        Assert.Equal(.5m, (await db.Responses.OrderBy(x => x.SubmittedAt).FirstAsync()).TrustAtSubmission);
    }

    [Fact]
    public async Task ExportAndLevelUseExpertRenderingWithoutLeakingKeysOrAddingTrust()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        using var expert = await Staff(f, admin, "expert");
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(0, d.VariantId, [], []));
        await Process(f, d.CaseId);
        using var exporter = await Staff(f, admin, "exporter");
        var rows = await exporter.GetFromJsonAsync<ExportRow[]>("/api/translations/export");
        Assert.Equal("Hello", Assert.Single(rows!).Translation); Assert.Equal(1, rows![0].Confirmed); Assert.Equal("confirmed", rows[0].Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.GetAsync("/api/translations/export")).StatusCode);
        var level = await CurrentLevel(student.Client);
        Assert.Equal(level.Id, (await CurrentLevel(student.Client)).Id);
        var json = await (await student.Client.PostAsJsonAsync("/api/learning/current", new { })).Content.ReadAsStringAsync();
        Assert.DoesNotContain("acceptedTexts", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Hello", json);
        var attempt = await AnswerLevel(student.Client, level, new TextExerciseAnswer("Hello"));
        Assert.True(attempt.Recognized);
        Assert.Equal(attempt, await AnswerLevel(student.Client, level, new TextExerciseAnswer("Hello")));
        Assert.Equal((1, 1), await Trust(f, student.Id));
    }

    internal static async Task Process(CoreFactory f, Guid caseId)
    { using var scope = f.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(caseId, default); }
    private static async Task<(int Correct, int Total)> Trust(CoreFactory f, Guid id)
    { using var scope = f.Services.CreateScope(); var p = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Participants.AsNoTracking().SingleAsync(x => x.Id == id); return (p.Correct, p.Total); }
    internal static async Task<HttpClient> Staff(CoreFactory f, HttpClient admin, string profile)
    {
        var account = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest(profile, profile));
        await Activate(admin, account.Id); var client = f.CreateClient(); await Login(client, account.Username); return client;
    }
}
