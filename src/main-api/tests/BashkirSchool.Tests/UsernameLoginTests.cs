using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class UsernameLoginTests
{
    [Fact]
    public async Task UsernameLoginCreatesParticipantAndResumesSameProfileButRejectsStaff()
    {
        using var f = new CoreFactory(usernameLogin: true);
        using var admin = await f.Bootstrap();
        using var client = f.CreateClient();
        var first = await Post<AccessTokenResponse>(client, "/auth/username", new { username = "Денис" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
        var profile = await client.GetFromJsonAsync<PlayerProfile>("/api/player/profile");
        Assert.Equal("Денис", profile!.Nick);
        var second = await Post<AccessTokenResponse>(client, "/auth/username", new { username = "Денис" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        Assert.Equal(profile.Id, (await client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Id);
        var assignedStudent = await f.Student(admin);
        using var schoolLogin = f.CreateClient();
        var schoolToken = await Post<AccessTokenResponse>(schoolLogin, "/auth/username", new { username = "student1" });
        schoolLogin.DefaultRequestHeaders.Authorization = new("Bearer", schoolToken.AccessToken);
        Assert.Equal(assignedStudent.Id,
            (await schoolLogin.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/auth/username", new { username = "admin" })).StatusCode);
    }

    [Fact]
    public async Task UsernameLoginIsDisabledUnlessConfigured()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.CreateClient().PostAsJsonAsync("/auth/username", new { username = "guest" })).StatusCode);
    }
}
