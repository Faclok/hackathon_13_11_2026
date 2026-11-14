using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task DeliveryAssignmentConfirmationAndRetryPersistOneVoteAndOutboxMessage()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        using var producer = await factory.Producer(admin);
        var delivery = await Deliver(producer);
        Assert.Equal(delivery, await Deliver(producer));
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        Assert.Equal(delivery.VariantId, assignment.VariantId);
        Assert.Equal("Һаумы", assignment.Original);
        Assert.Equal("Hello", assignment.Translation);
        Assert.Equal(assignment, await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { }));
        var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        Assert.Equal(receipt, await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm)));
        var stats = await student.Client.GetFromJsonAsync<ReviewStatistics>($"/api/cases/{delivery.CaseId}/statistics");
        Assert.Equal(1, stats!.UniqueParticipants);
        Assert.Equal(new VariantStatistics(delivery.VariantId, 1, 1m), Assert.Single(stats.Variants));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(1, await db.Responses.CountAsync());
        Assert.Equal(1, await db.Outbox.CountAsync());
        Assert.Equal(.5m, (await db.Responses.SingleAsync()).TrustAtSubmission);
        var different = await student.Client.PostAsJsonAsync($"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Skip), Json);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
    }

    [Fact]
    public async Task CorrectionKeepsSeparateVariantStatisticsAndDoesNotChangeShownCandidate()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin); using var producer = await factory.Producer(admin);
        var delivery = await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "Здравствуйте"));
        var stats = await student.Client.GetFromJsonAsync<ReviewStatistics>($"/api/cases/{delivery.CaseId}/statistics");
        Assert.Equal(1, stats!.UniqueParticipants);
        Assert.Equal(2, stats.Variants.Count);
        Assert.Equal(new VariantStatistics(delivery.VariantId, 0, 0m), stats.Variants.Single(x => x.VariantId == delivery.VariantId));
        Assert.Equal(1m, stats.Variants.Single(x => x.VariantId != delivery.VariantId).SupportRatio);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(delivery.VariantId, (await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Cases.SingleAsync()).ShownVariantId);
    }

    [Fact]
    public async Task SkipDoesNotCountAndExpiredReservationAllowsAnotherParticipant()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        var second = await factory.Student(admin, "2");
        using var producer = await factory.Producer(admin); var delivery = await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(RequiredParticipants: 2,
            MinimumSchools: 1, MinimumClasses: 1, MaximumSchoolShare: 1, MaximumClassShare: .5m));
        var first = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{first.Id}/response", new SubmitReviewRequest(ReviewAction.Skip));
        var stats = await student.Client.GetFromJsonAsync<ReviewStatistics>($"/api/cases/{delivery.CaseId}/statistics");
        Assert.Equal(0, stats!.UniqueParticipants);
        Assert.Null(Assert.Single(stats.Variants).SupportRatio);
        var reserved = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        factory.Clock.Now = factory.Clock.Now.AddMinutes(30);
        var expired = await second.Client.PostAsJsonAsync($"/api/reviews/{reserved.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm), Json);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task DeliveryKeyWithDifferentPayloadConflictsWithoutCreatingAnotherCase()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap(); using var producer = await factory.Producer(admin);
        await Deliver(producer);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/deliveries");
        request.Headers.Add("Idempotency-Key", "one");
        request.Content = JsonContent.Create(new DeliverContentRequest("fragment-one", "Һаумы", "ba-ru", "Changed", new("document-1")));
        Assert.Equal(HttpStatusCode.Conflict, (await producer.SendAsync(request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Cases.CountAsync());
    }
}
