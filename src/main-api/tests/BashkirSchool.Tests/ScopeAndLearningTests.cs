using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ScopeAndLearningTests
{
    [Fact]
    public async Task ExpertAccessIsUniversalWhileExportRemainsCaseScoped()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); await f.Student(admin);
        using var producer = await f.Producer(admin); var a = await Deliver(producer, "a"); var b = await Deliver(producer, "b");
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var teacherId = (await db.Users.AsNoTracking().SingleAsync(x => x.UserName == "teacher1")).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{teacherId}/access", new AssignAccessRequest("expert", CaseId: a.CaseId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{teacherId}/access", new AssignAccessRequest("exporter", CaseId: a.CaseId))).StatusCode);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/expert/cases/{a.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/expert/cases/{b.CaseId}")).StatusCode);
        var export = await teacher.GetFromJsonAsync<ExportRow[]>("/api/translations/export?includeDisputed=true");
        Assert.Equal("fragment-a", Assert.Single(export!).FragmentId);
        Assert.Empty((await teacher.GetFromJsonAsync<ExportRow[]>("/api/translations/export?includeDisputed=true&offset=1"))!);
        var grants = await admin.GetFromJsonAsync<AccessAssignmentResponse[]>($"/api/accounts/{teacherId}/access", Json);
        var expertGrant = grants!.Single(x => x.Profile == "expert");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/access/{expertGrant.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/expert/cases/{a.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/translations/export?includeDisputed=true")).StatusCode);
    }

    [Fact]
    public async Task ExpertRejectsForeignAndDuplicateVariantsAndAdministratorCanReadCases()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); using var producer = await f.Producer(admin);
        var a = await Deliver(producer, "a"); var b = await Deliver(producer, "b");
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/expert/cases/{a.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await expert.PostAsJsonAsync($"/api/expert/cases/{a.CaseId}/resolution", new ResolveCaseRequest(0, b.VariantId, [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await expert.PostAsJsonAsync($"/api/expert/cases/{a.CaseId}/resolution", new ResolveCaseRequest(0, a.VariantId, [a.VariantId], []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await expert.PostAsJsonAsync($"/api/expert/cases/{a.CaseId}/resolution", new ResolveCaseRequest(0, a.VariantId, [], [a.VariantId]))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.False(await db.Resolutions.AnyAsync()); Assert.False(await db.Cases.AnyAsync(x => x.ExpertClosed));
    }

    [Fact]
    public async Task AttemptIsOwnOnlyUnknownTextIsUnrecognizedAndDoesNotBecomeAVote()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var first = await f.Student(admin); var second = await f.Student(admin, "2");
        using var producer = await f.Producer(admin); var d = await Deliver(producer);
        using var expert = await ResolutionFlowTests.Staff(f, admin, "expert");
        await Post<ResolutionResponse>(expert, $"/api/expert/cases/{d.CaseId}/resolution", new ResolveCaseRequest(0, d.VariantId, [], []));
        var lesson = await first.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"); var exercise = Assert.Single(lesson!.Exercises);
        Assert.Equal(HttpStatusCode.NotFound, (await second.Client.PostAsJsonAsync($"/api/exercises/{exercise.Id}/attempt", new SubmitAttemptRequest("Hello"))).StatusCode);
        var result = await Post<AttemptResponse>(first.Client, $"/api/exercises/{exercise.Id}/attempt", new SubmitAttemptRequest("Unknown synonym"));
        Assert.False(result.Recognized);
        Assert.Equal(HttpStatusCode.Conflict, (await first.Client.PostAsJsonAsync($"/api/exercises/{exercise.Id}/attempt", new SubmitAttemptRequest("Hello"))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.False(await db.Responses.AnyAsync()); Assert.False(await db.Judgments.AnyAsync());
        Assert.All(await db.Participants.ToArrayAsync(), p => { Assert.Equal(0, p.Correct); Assert.Equal(0, p.Total); });
    }

    [Fact]
    public async Task OwnPasswordChangeChecksOldPasswordAndRevokesAccessAndRefresh()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        var session = await Post<AccessTokenResponse>(student.Client, "/auth/sign-in", new SignInRequest("student1", Password));
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync("/api/credentials/change", new ChangePasswordRequest("wrong", "New.Password123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/credentials/change", new ChangePasswordRequest(Password, "New.Password123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.Client.GetAsync("/api/sessions")).StatusCode);
        using var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/refresh", new RefreshSessionRequest(session.RefreshToken!))).StatusCode);
        await Login(student.Client, "student1", "New.Password123!");
        Assert.Equal(HttpStatusCode.OK, (await student.Client.GetAsync("/api/sessions")).StatusCode);
    }
}
