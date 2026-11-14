using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class AllocationTests
{
    [Fact]
    public async Task ReservationsEnforceClassCapAndExpirationReleasesCapacity()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var first = await factory.Student(admin);
        using var teacher = factory.CreateClient(); await Login(teacher, "teacher1");
        var second = await Post<AccountResponse>(teacher, $"/api/classes/{first.ClassId}/students", new CreateStudentRequest("second"));
        await Activate(teacher, second.Id);
        using var secondClient = factory.CreateClient(); await Login(secondClient, "second");
        using var producer = await factory.Producer(admin); await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(RequiredParticipants: 2,
            MinimumSchools: 1, MinimumClasses: 2, MaximumSchoolShare: 1, MaximumClassShare: .5m));
        var reserved = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await secondClient.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        factory.Clock.Now = factory.Clock.Now.AddMinutes(30);
        var reassigned = await Post<AssignmentResponse>(secondClient, "/api/reviews/next", new { });
        Assert.Equal(reserved.CaseId, reassigned.CaseId);
        Assert.NotEqual(reserved.Id, reassigned.Id);
    }

    [Fact]
    public async Task NewDayAssignmentKeepsHistoryButOneUniqueOpinionAndSkipDoesNotEraseIt()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin); using var producer = await factory.Producer(admin); var delivery = await Deliver(producer);
        var first = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{first.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        factory.Clock.Now = factory.Clock.Now.AddDays(1);
        var second = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
        Assert.NotEqual(first.Id, second.Id);
        await Post<ReviewReceipt>(student.Client, $"/api/reviews/{second.Id}/response", new SubmitReviewRequest(ReviewAction.Skip));
        var stats = await student.Client.GetFromJsonAsync<ReviewStatistics>($"/api/cases/{delivery.CaseId}/statistics");
        Assert.Equal(1, stats!.UniqueParticipants);
        Assert.Equal(new VariantStatistics(delivery.VariantId, 1, 1m), Assert.Single(stats.Variants));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Responses.CountAsync());
    }

    [Fact]
    public async Task ParticipantCannotSubmitAnotherParticipantsAssignment()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var first = await factory.Student(admin); var second = await factory.Student(admin, "2");
        using var producer = await factory.Producer(admin); await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        var result = await second.Client.PostAsJsonAsync($"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm), Json);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Responses.CountAsync());
    }
}
