using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ClassInvitationTests
{
    [PostgresFact]
    public async Task MigrationPreservesAccountsAndEnforcesOnePendingInvitationPerClass()
    {
        await using var database = await TestDatabase.Create();
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261114130314_AudioCacheIdentity");
        var teacher = new Account { Id = Guid.NewGuid(), UserName = "teacher" };
        var student = new Account { Id = Guid.NewGuid(), UserName = "independent", SelfRegistered = true };
        // Seed only columns present at the historical migration. The current Account
        // model also contains expert preferences added by a later migration.
        foreach (var account in new[] { teacher, student })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AspNetUsers" ("Id", "Active", "SessionVersion", "UserName", "SelfRegistered",
                    "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ({account.Id}, TRUE, 0, {account.UserName}, {account.SelfRegistered}, FALSE, FALSE, FALSE, FALSE, 0)
                """);
        var school = new School { Name = "School" };
        var studyClass = new StudyClass { SchoolId = school.Id, TeacherId = teacher.Id, Name = "Class" };
        db.AddRange(school, studyClass, new Participant { Id = student.Id });
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.True((await db.Users.SingleAsync(x => x.Id == student.Id)).SelfRegistered);
        Assert.Equal(studyClass.Id, (await db.Classes.SingleAsync()).Id);
        Assert.False(db.Database.HasPendingModelChanges());
        db.ClassInvitations.Add(new() { ClassId = studyClass.Id, TeacherId = teacher.Id, ParticipantId = student.Id,
            Status = ClassInvitationStatus.Declined, CreatedAt = DateTimeOffset.UtcNow });
        db.ClassInvitations.Add(new() { ClassId = studyClass.Id, TeacherId = teacher.Id, ParticipantId = student.Id,
            CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ClassInvitations.Add(new() { ClassId = studyClass.Id, TeacherId = teacher.Id, ParticipantId = student.Id,
            CreatedAt = DateTimeOffset.UtcNow });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_ClassInvitations_ClassId_ParticipantId", Assert.IsType<Npgsql.PostgresException>(error.InnerException).ConstraintName);
    }

    [Fact]
    public async Task AcceptanceEnrollsOnlyAfterConsentAndCancelsOtherInvitations()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        var schoolStudent = await f.Student(admin);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        var secondClass = await Post<IdResponse>(teacher, "/api/classes", new CreateClassRequest(
            (await schoolStudent.Client.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.SchoolId!.Value, "Second class"));
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var invitation = await Invite(teacher, schoolStudent.ClassId, account.Username);
        var duplicate = await Invite(teacher, schoolStudent.ClassId, account.Username.ToUpperInvariant());
        Assert.Equal(invitation.Id, duplicate.Id);
        Assert.Equal(ClassInvitationStatus.Pending, invitation.Status);
        Assert.Null((await person.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.ClassId);
        Assert.Equal(account.Id, Assert.Single(await person.GetFromJsonAsync<ClassInvitationResponse[]>("/api/me/class-invitations", Json) ?? []).ParticipantId);
        var other = await Invite(teacher, secondClass.Id, account.Username);
        var accepted = await Respond(person, invitation.Id, true);
        Assert.Equal(ClassInvitationStatus.Accepted, accepted.Status);
        Assert.Equal(f.Clock.Now, accepted.RespondedAt);
        Assert.Equal(accepted, await Respond(person, invitation.Id, true));
        var profile = await person.GetFromJsonAsync<MyProfileResponse>("/api/me");
        Assert.Equal(schoolStudent.ClassId, profile!.ClassId); Assert.Equal(invitation.SchoolId, profile.SchoolId);
        var invitations = await person.GetFromJsonAsync<ClassInvitationResponse[]>("/api/me/class-invitations", Json);
        Assert.Equal(ClassInvitationStatus.Cancelled, Assert.Single(invitations!, x => x.Id == other.Id).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await person.PostAsJsonAsync($"/api/me/class-invitations/{other.Id}/response", new RespondToClassInvitationRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await teacher.PostAsJsonAsync($"/api/classes/{secondClass.Id}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(schoolStudent.ClassId, (await db.Enrollments.SingleAsync(x => x.ParticipantId == account.Id)).ClassId);
        Assert.Equal(1, await db.Audit.CountAsync(x => x.TargetId == invitation.Id && x.Action == "class.invitation.accepted"));
        Assert.Equal(2, await db.Access.CountAsync(x => x.SubjectId == account.Id));
        // The established teacher recovery path becomes available only after membership is confirmed.
        await Post<PasswordSetupResponse>(teacher, $"/api/accounts/{account.Id}/password-link", new { });
    }

    [Fact]
    public async Task DeclineLeavesParticipantIndependentAndAllowsANewInvitation()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var invitation = await Invite(teacher, student.ClassId, account.Username);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/accounts/{account.Id}/password-link", new { })).StatusCode);
        var declined = await Respond(person, invitation.Id, false);
        Assert.Equal(ClassInvitationStatus.Declined, declined.Status);
        Assert.Equal(declined, await Respond(person, invitation.Id, false));
        Assert.Null((await person.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.ClassId);
        Assert.Equal(HttpStatusCode.Conflict, (await person.PostAsJsonAsync($"/api/me/class-invitations/{invitation.Id}/response", new RespondToClassInvitationRequest(true))).StatusCode);
        var replacement = await Invite(teacher, student.ClassId, account.Username);
        Assert.NotEqual(invitation.Id, replacement.Id); Assert.Equal(ClassInvitationStatus.Pending, replacement.Status);
        var visible = await teacher.GetFromJsonAsync<ClassInvitationResponse[]>($"/api/classes/{student.ClassId}/invitations", Json);
        Assert.Equal(2, visible!.Length);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.False(await db.Enrollments.AnyAsync(x => x.ParticipantId == account.Id));
    }

    [Fact]
    public async Task OtherUsersCannotInviteReadOrAcceptOutsideTheirScope()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        var strangerClass = await f.Student(admin, "2");
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var strangerTeacher = f.CreateClient(); await Login(strangerTeacher, "teacher2");
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var invitation = await Invite(teacher, student.ClassId, account.Username);
        foreach (var caller in new[] { teacher, student.Client, person, admin })
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsJsonAsync($"/api/classes/{strangerClass.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerTeacher.PostAsJsonAsync($"/api/classes/{student.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerTeacher.GetAsync($"/api/classes/{student.ClassId}/invitations")).StatusCode);
        Assert.Empty((await student.Client.GetFromJsonAsync<ClassInvitationResponse[]>("/api/me/class-invitations", Json))!);
        foreach (var caller in new[] { teacher, student.Client, strangerTeacher })
            Assert.Equal(HttpStatusCode.NotFound, (await caller.PostAsJsonAsync($"/api/me/class-invitations/{invitation.Id}/response", new RespondToClassInvitationRequest(true))).StatusCode);
        using var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/class-invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await person.GetAsync("/api/me/class-invitations?limit=101")).StatusCode);
        Assert.Null((await person.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.ClassId);
    }

    [Fact]
    public async Task AcceptanceRechecksTeacherPermissionAndParticipantPrivileges()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var invitation = await Invite(teacher, student.ClassId, account.Username);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{account.Id}/access", new AssignAccessRequest("expert"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync($"/api/me/class-invitations/{invitation.Id}/response", new RespondToClassInvitationRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/classes/{student.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var expertAccess = await db.Access.SingleAsync(x => x.SubjectId == account.Id && x.ProfileId == "expert");
        db.Access.Remove(expertAccess);
        var teacherAccount = await db.Users.SingleAsync(x => x.UserName == "teacher1"); teacherAccount.Active = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync($"/api/me/class-invitations/{invitation.Id}/response", new RespondToClassInvitationRequest(true))).StatusCode);
        Assert.Equal(ClassInvitationStatus.Declined, (await Respond(person, invitation.Id, false)).Status);
        db.ChangeTracker.Clear();
        Assert.False(await db.Enrollments.AnyAsync(x => x.ParticipantId == account.Id));
    }

    private static Task<ClassInvitationResponse> Invite(HttpClient teacher, Guid classId, string username) =>
        Post<ClassInvitationResponse>(teacher, $"/api/classes/{classId}/invitations", new InviteStudentRequest(username));
    private static Task<ClassInvitationResponse> Respond(HttpClient person, Guid invitationId, bool accept) =>
        Post<ClassInvitationResponse>(person, $"/api/me/class-invitations/{invitationId}/response", new RespondToClassInvitationRequest(accept));
}
