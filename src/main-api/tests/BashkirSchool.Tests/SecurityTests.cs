using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class SecurityTests
{
    [Fact]
    public async Task TeacherCannotResetStudentInAnotherTeachersClass()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var first = await factory.Student(admin); var second = await factory.Student(admin, "2");
        using var teacher = factory.CreateClient(); await Login(teacher, "teacher1");
        var result = await teacher.PostAsJsonAsync($"/api/accounts/{second.Id}/password-link", new { });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        var own = await Post<PasswordSetupResponse>(teacher, $"/api/accounts/{first.Id}/password-link", new { });
        Assert.Equal(first.Id, own.AccountId);
        var strangerClass = await teacher.PostAsJsonAsync($"/api/classes/{second.ClassId}/students", new CreateStudentRequest("outsider"));
        Assert.Equal(HttpStatusCode.Forbidden, strangerClass.StatusCode);
    }

    [Fact]
    public async Task ReissuedPasswordLinkInvalidatesOldLinkAndPasswordChangeRevokesJwt()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        using var teacher = factory.CreateClient(); await Login(teacher, "teacher1");
        var old = await Post<PasswordSetupResponse>(teacher, $"/api/accounts/{student.Id}/password-link", new { });
        var latest = await Post<PasswordSetupResponse>(teacher, $"/api/accounts/{student.Id}/password-link", new { });
        var oldResult = await teacher.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(student.Id, old.Token, Password));
        Assert.Equal(HttpStatusCode.BadRequest, oldResult.StatusCode);
        var changed = await teacher.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(student.Id, latest.Token, Password));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        var reused = await teacher.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(student.Id, latest.Token, Password));
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        await Login(student.Client, "student1");
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
    }

    [Fact]
    public async Task PermissionAndGlobalScopeCannotBeCombinedFromDifferentAssignments()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var teacherId = (await db.Users.SingleAsync(x => x.UserName == "teacher1")).Id;
        db.Access.Add(new() { SubjectId = teacherId, ProfileId = "expert", Scope = AccessScope.Global });
        await db.SaveChangesAsync();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        Assert.False(await checker.HasAsync(teacherId, "students.create", AccessScope.School, Guid.NewGuid(), default));
        Assert.False(await checker.HasAsync(student.Id, "expert.translation.resolve", AccessScope.Global, null, default));
        var policy = await student.Client.PostAsJsonAsync("/api/policies/distribution", new DistributionPolicyRequest());
        Assert.Equal(HttpStatusCode.Forbidden, policy.StatusCode);
    }

    [Fact]
    public async Task BlockingAccountRevokesExistingJwtAndBootstrapCannotRunAgain()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap(); var student = await factory.Student(admin);
        var blocked = await admin.PutAsJsonAsync($"/api/accounts/{student.Id}/status", new AccountStatusRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        using var scope = factory.Services.CreateScope();
        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync("other-admin", Password, default));
    }

    [Fact]
    public async Task AnonymousAndForgedTrustInputsAreRejected()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap(); var student = await factory.Student(admin);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        var forged = await student.Client.PostAsJsonAsync($"/api/reviews/{Guid.NewGuid()}/response", new { action = "Confirm", trustAtSubmission = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }
}
