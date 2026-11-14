using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ExpertWorkspaceTests
{
    [Fact]
    public async Task TeacherRequestPrecedesDisputedCaseAndBypassesPersonalThresholds()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var first = await f.Student(admin); var second = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        var disputed = await Deliver(producer, "community");
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(2, .8m));
        var firstAssignment = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{firstAssignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        var secondAssignment = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(second.Client, $"/api/reviews/{secondAssignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "Other"));
        var teacher = await Deliver(producer, "teacher");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            db.Audit.Add(new() { ActorId = Guid.NewGuid(), TargetId = teacher.CaseId, Action = "lms.expert.requested", Reason = "Проверьте вариант", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var queue = (await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!;
        Assert.Equal(2, queue.Length);
        Assert.Equal(teacher.CaseId, queue[0].CaseId);
        Assert.True(queue[0].TeacherRequested);
        Assert.Equal(disputed.CaseId, queue[1].CaseId);
        await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(300, .8m));
        Assert.Equal(teacher.CaseId, Assert.Single((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!).CaseId);
    }

    [Fact]
    public async Task DisputedCaseCanBeClaimedOnceAndNeverReturnsAfterDecision()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var first = await f.Student(admin); var second = await f.Student(admin, "2");
        using var producer = await f.Producer(admin); var delivery = await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(2, .8m, 1, 1, 1, 1));
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        Assert.Equal(HttpStatusCode.OK, (await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(2, .8m))).StatusCode);
        Assert.Empty((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        var firstAssignment = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{firstAssignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        var secondAssignment = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(second.Client, $"/api/reviews/{secondAssignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "Other"));
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(delivery.CaseId, default);
        Assert.Single((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Equal(300, (await first.Client.GetFromJsonAsync<ExpertPreferencesResponse>("/api/expert/preferences"))!.RequiredParticipants);
        Assert.Equal(HttpStatusCode.OK, (await first.Client.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(2, .8m))).StatusCode);
        Assert.Single((await first.Client.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.Client.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(0, .8m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(2, .5m))).StatusCode);
        Assert.Empty((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Equal(HttpStatusCode.OK, (await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(2, .8m))).StatusCode);
        var taken = await Post<ExpertWorkItem>(expert, $"/api/expert/cases/{delivery.CaseId}/claim", new { });
        Assert.False(taken.Completed);
        Assert.Empty((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Single((await expert.GetFromJsonAsync<ExpertWorkItem[]>("/api/expert/work"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await expert.DeleteAsync($"/api/expert/cases/{delivery.CaseId}/claim")).StatusCode);
        Assert.Single((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        await Post<ExpertWorkItem>(expert, $"/api/expert/cases/{delivery.CaseId}/claim", new { });
        var otherAccount = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("expert2", "expert"));
        await Activate(admin, otherAccount.Id);
        using var otherExpert = f.CreateClient();
        await Login(otherExpert, otherAccount.Username);
        Assert.Equal(HttpStatusCode.Conflict, (await otherExpert.PostAsJsonAsync($"/api/expert/cases/{delivery.CaseId}/claim", new { })).StatusCode);
        var view = await expert.GetFromJsonAsync<ExpertCaseResponse>($"/api/expert/cases/{delivery.CaseId}");
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{delivery.CaseId}/resolution",
            new ResolveCaseRequest(view!.Revision, delivery.VariantId, [], []));
        var work = (await expert.GetFromJsonAsync<ExpertWorkItem[]>("/api/expert/work"))!;
        Assert.True(Assert.Single(work).Completed);
        Assert.Empty((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await expert.PostAsJsonAsync($"/api/expert/cases/{delivery.CaseId}/claim", new { })).StatusCode);
    }

    [Fact]
    public async Task ReachingSupportThresholdKeepsCaseOutOfExpertQueue()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var student = await f.Student(admin); using var producer = await f.Producer(admin);
        var delivery = await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(1, .8m, 1, 1, 1, 1));
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(delivery.CaseId, default);
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        Assert.Equal(HttpStatusCode.OK, (await expert.PutAsJsonAsync("/api/expert/preferences", new ExpertPreferencesResponse(1, .8m))).StatusCode);
        Assert.Empty((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await expert.PostAsJsonAsync($"/api/expert/cases/{delivery.CaseId}/claim", new { })).StatusCode);
    }
}
