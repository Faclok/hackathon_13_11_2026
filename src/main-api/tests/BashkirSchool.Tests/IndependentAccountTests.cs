using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class IndependentAccountTests
{
    [Fact]
    public async Task RegistrationCreatesOnlyParticipantAndOwnAccountWithoutSchoolAndCanSignIn()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); using var person = f.CreateClient();
        var registered = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, registered.Username);
        var profile = await person.GetFromJsonAsync<MyProfileResponse>("/api/me");
        Assert.Equal(registered.Id, profile!.Id); Assert.Equal("independent", profile.Username);
        Assert.Null(profile.SchoolId); Assert.Null(profile.ClassId);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var account = await db.Users.SingleAsync(x => x.Id == registered.Id);
        Assert.True(account.SelfRegistered); Assert.Null(account.Email);
        Assert.False(await db.Enrollments.AnyAsync()); Assert.False(await db.Schools.AnyAsync()); Assert.False(await db.Classes.AnyAsync());
        var grants = await db.Access.Where(x => x.SubjectId == registered.Id).ToArrayAsync();
        Assert.Equal(new[] { "account", "participant" }, grants.Select(x => x.ProfileId).Order().ToArray());
        Assert.All(grants, g => Assert.Equal(AccessScope.Own, g.Scope));
        var participant = await db.Participants.SingleAsync(x => x.Id == registered.Id);
        Assert.Equal(0, participant.Correct); Assert.Equal(0, participant.Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync("/api/staff", new CreateStaffRequest("fake-expert", "expert"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await person.GetAsync("/api/classes")).StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsWeakPasswordDuplicateLoginAndPrivilegeInjectionWithoutPartialAccount()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); using var person = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await person.PostAsJsonAsync("/auth/register", new RegisterAccountRequest("weak", "short"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await person.PostAsJsonAsync("/auth/register", new { username = "forged", password = Password, profile = "administrator" })).StatusCode);
        var valid = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        Assert.Equal(HttpStatusCode.Conflict, (await person.PostAsJsonAsync("/auth/register", new RegisterAccountRequest("INDEPENDENT", Password))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Equal(1, await db.Participants.CountAsync());
        Assert.False(await db.Users.AnyAsync(x => x.UserName == "weak" || x.UserName == "forged"));
        Assert.Equal(1, await db.Audit.CountAsync(x => x.Action == "account.self.registered" && x.TargetId == valid.Id));
    }

    [Fact]
    public async Task PublicRegistrationIsRateLimitedWithoutCreatingRejectedAccounts()
    {
        using var f = new CoreFactory(registrationLimit: 2); using var admin = await f.Bootstrap(); using var person = f.CreateClient();
        await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("first", Password));
        await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("second", Password));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await person.PostAsJsonAsync("/auth/register", new RegisterAccountRequest("third", Password))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.UserName == "third")); Assert.Equal(2, await db.Participants.CountAsync());
    }

    [Fact]
    public async Task IndependentVotesCountButCannotInventSchoolCoverageOrCommunityResult()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); using var producer = await f.Producer(admin);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(2, .8m, 1, 1, 1, 1));
        var d = await Deliver(producer); Guid firstId = default;
        for (var i = 0; i < 2; i++)
        {
            using var client = f.CreateClient(); var person = await Post<AccountResponse>(client, "/auth/register", new RegisterAccountRequest("independent" + i, Password));
            if (i == 0) firstId = person.Id;
            await Login(client, person.Username);
            var assignment = await Post<AssignmentResponse>(client, "/api/reviews/next", new { });
            await Post<ReviewReceipt>(client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        }
        await ResolutionFlowTests.Process(f, d.CaseId);
        using var reader = f.CreateClient(); await Login(reader, "independent0");
        var stats = await reader.GetFromJsonAsync<CaseStatisticsResponse>($"/api/cases/{d.CaseId}/statistics");
        Assert.Equal(2, stats!.UniqueParticipants); Assert.Equal(1m, stats.CompletionRatio); Assert.Equal(1m, Assert.Single(stats.Variants).SupportRatio);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.All(await db.Assignments.ToArrayAsync(), a => { Assert.Null(a.SchoolId); Assert.Null(a.ClassId); });
        var c = await db.Cases.SingleAsync(); Assert.True(c.NeedsExpert); Assert.Null(c.ResolutionId);
        Assert.Equal(0, (await db.Participants.SingleAsync(x => x.Id == firstId)).Total);
        Assert.False(await db.Schools.AnyAsync()); Assert.False(await db.Classes.AnyAsync()); Assert.False(await db.Enrollments.AnyAsync());
    }

    [Fact]
    public async Task MixedSchoolAndIndependentVotesShareOneResultAndKeepRealGroupSnapshots()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var independent = f.CreateClient(); var person = await Post<AccountResponse>(independent, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(independent, person.Username);
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        await Post<IdResponse>(admin, "/api/policies/distribution", new DistributionPolicyRequest(2, .8m, 1, 1, 1, 1));
        foreach (var client in new[] { independent, student.Client })
        {
            var assignment = await Post<AssignmentResponse>(client, "/api/reviews/next", new { });
            await Post<ReviewReceipt>(client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        }
        await ResolutionFlowTests.Process(f, d.CaseId);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(ResolutionKind.Community, (await db.Resolutions.SingleAsync()).Kind);
        Assert.Equal(2, await db.TrustAssessments.CountAsync());
        var snapshot = await db.Assignments.SingleAsync(x => x.ParticipantId == person.Id); Assert.Null(snapshot.SchoolId); Assert.Null(snapshot.ClassId);
        Assert.Equal(student.ClassId, (await db.Assignments.SingleAsync(x => x.ParticipantId == student.Id)).ClassId);
        var level = await CurrentLevel(independent); Assert.Equal(1, level.Number); Assert.Equal("translation-text", level.Question.Kind);
        var profile = await student.Client.GetFromJsonAsync<MyProfileResponse>("/api/me"); Assert.Equal(student.ClassId, profile!.ClassId); Assert.NotNull(profile.SchoolId);
    }

    [Fact]
    public async Task AdministratorRecoveryIsAuditedRotatesLinksAndRevokesPreviousSessionsOnCompletion()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var session = await Post<AccessTokenResponse>(person, "/auth/sign-in", new SignInRequest(account.Username, Password));
        var request = new RecoverIndependentAccountRequest("Identity checked manually by administrator, ticket 123.");
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync($"/api/accounts/{account.Id}/recovery-link", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/accounts/{account.Id}/recovery-link", new RecoverIndependentAccountRequest(""))).StatusCode);
        var old = await Post<PasswordSetupResponse>(admin, $"/api/accounts/{account.Id}/recovery-link", request);
        var latest = await Post<PasswordSetupResponse>(admin, $"/api/accounts/{account.Id}/recovery-link", request);
        using var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(account.Id, old.Token, "Recovered.Password123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(account.Id, latest.Token, "Recovered.Password123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await person.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/refresh", new RefreshSessionRequest(session.RefreshToken!))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(account.Id, latest.Token, "Recovered.Password123!"))).StatusCode);
        await Login(person, account.Username, "Recovered.Password123!");
        Assert.Equal(account.Id, (await person.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.Id);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var audit = await db.Audit.Where(x => x.Action == "independent.password.link.issued").ToArrayAsync();
        Assert.Equal(2, audit.Length); Assert.All(audit, a => { Assert.Equal(account.Id, a.TargetId); Assert.Equal(request.VerificationNote, a.Reason); Assert.NotEqual(account.Id, a.ActorId); });
        Assert.DoesNotContain(latest.Token, string.Join(" ", audit.Select(x => x.Reason)));
    }

    [Fact]
    public async Task IndependentRecoveryCannotResetSchoolStudentsStaffOrPromotedIndependentAccounts()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var person = f.CreateClient(); var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        var request = new RecoverIndependentAccountRequest("Manual check reference.");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/accounts/{account.Id}/recovery-link", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/accounts/{student.Id}/recovery-link", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{account.Id}/access", new AssignAccessRequest("expert"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/accounts/{account.Id}/recovery-link", request)).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.False(await db.Audit.AnyAsync(x => x.Action == "independent.password.link.issued"));
    }
}
