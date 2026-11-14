using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BashkirSchool.Application;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class SessionAndDirectoryTests
{
    [Fact]
    public async Task RefreshRotatesAndReplayRevokesTheSessionFamily()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        var initial = await Post<AccessTokenResponse>(student.Client, "/auth/sign-in", new SignInRequest("student1", Password));
        var rotated = await Post<AccessTokenResponse>(student.Client, "/auth/refresh", new RefreshSessionRequest(initial.RefreshToken!));
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken); Assert.Equal(initial.SessionId, rotated.SessionId);
        student.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await student.Client.GetAsync("/api/sessions")).StatusCode);
        var replay = await student.Client.PostAsJsonAsync("/auth/refresh", new RefreshSessionRequest(initial.RefreshToken!));
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.Client.GetAsync("/api/sessions")).StatusCode);
        using var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/refresh", new RefreshSessionRequest(rotated.RefreshToken!))).StatusCode);
    }

    [Fact]
    public async Task SessionRevocationIsOwnOnlyAndSchoolListsAreScopedWithoutTrust()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var first = await f.Student(admin); var second = await f.Student(admin, "2");
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        var classes = await teacher.GetFromJsonAsync<ClassResponse[]>("/api/classes");
        Assert.Equal(first.ClassId, Assert.Single(classes!).Id);
        var students = await teacher.GetFromJsonAsync<AccountResponse[]>($"/api/classes/{first.ClassId}/students");
        Assert.Equal(first.Id, Assert.Single(students!).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/classes/{second.ClassId}/students")).StatusCode);
        var sessions = await first.Client.GetFromJsonAsync<SessionResponse[]>("/api/sessions");
        var ownSession = Assert.Single(sessions!);
        Assert.Equal(HttpStatusCode.NotFound, (await second.Client.DeleteAsync($"/api/sessions/{ownSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.Client.DeleteAsync($"/api/sessions/{ownSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.Client.GetAsync("/api/sessions")).StatusCode);
        var body = await teacher.GetStringAsync($"/api/classes/{first.ClassId}/students");
        Assert.DoesNotContain("trust", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }
}
