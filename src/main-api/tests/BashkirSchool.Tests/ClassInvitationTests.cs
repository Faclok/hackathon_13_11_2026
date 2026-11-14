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
    public async Task TeacherAddsRegisteredStudentImmediatelyByUsername()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var schoolStudent = await f.Student(admin);
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        var updated = await person.PutAsJsonAsync("/api/me", new UpdateMyProfileRequest("Айгуль", "Иванова"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var player = await person.GetFromJsonAsync<PlayerProfile>("/api/player/profile");
        Assert.Equal("Айгуль", player!.FirstName); Assert.Equal("Иванова", player.LastName);
        Assert.Equal(100, player.Coins);
        using (var lookup = await teacher.GetAsync("/api/lms-source/participants/lookup?username=INDEPENDENT"))
        {
            Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
            Assert.Contains("Айгуль Иванова", await lookup.Content.ReadAsStringAsync());
        }
        using (var names = await teacher.GetAsync($"/api/lms-source/participants/names?ids={account.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, names.StatusCode);
            Assert.Contains("Айгуль Иванова", await names.Content.ReadAsStringAsync());
        }
        var invitation = await Invite(teacher, schoolStudent.ClassId, account.Username.ToUpperInvariant());
        Assert.Equal(ClassInvitationStatus.Accepted, invitation.Status);
        Assert.Equal(f.Clock.Now, invitation.RespondedAt);
        Assert.Equal(schoolStudent.ClassId, (await person.GetFromJsonAsync<MyProfileResponse>("/api/me"))!.ClassId);
        Assert.Equal(invitation.Id, (await Invite(teacher, schoolStudent.ClassId, account.Username)).Id);
        Assert.Equal(HttpStatusCode.Conflict,
            (await person.PostAsJsonAsync($"/api/me/class-invitations/{invitation.Id}/response", new RespondToClassInvitationRequest(false))).StatusCode);
        var roster = await teacher.GetFromJsonAsync<AccountResponse[]>($"/api/classes/{schoolStudent.ClassId}/students");
        Assert.Equal("Айгуль Иванова", Assert.Single(roster!, x => x.Id == account.Id).DisplayName);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(1, await db.Enrollments.CountAsync(x => x.ParticipantId == account.Id));
        await Post<PasswordSetupResponse>(teacher, $"/api/accounts/{account.Id}/password-link", new { });
    }

    [Fact]
    public async Task TeacherCannotAddStudentToSecondClassOrOutsideOwnScope()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var schoolStudent = await f.Student(admin);
        var other = await f.Student(admin, "2");
        using var teacher = f.CreateClient(); await Login(teacher, "teacher1");
        using var outsider = f.CreateClient(); await Login(outsider, "teacher2");
        using var person = f.CreateClient();
        var account = await Post<AccountResponse>(person, "/auth/register", new RegisterAccountRequest("independent", Password));
        await Login(person, account.Username);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsJsonAsync($"/api/classes/{schoolStudent.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        await Invite(teacher, schoolStudent.ClassId, account.Username);
        Assert.Equal(HttpStatusCode.Conflict,
            (await outsider.PostAsJsonAsync($"/api/classes/{other.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await person.PostAsJsonAsync($"/api/classes/{schoolStudent.ClassId}/invitations", new InviteStudentRequest(account.Username))).StatusCode);
    }

    private static Task<ClassInvitationResponse> Invite(HttpClient teacher, Guid classId, string username) =>
        Post<ClassInvitationResponse>(teacher, $"/api/classes/{classId}/invitations", new InviteStudentRequest(username));
}
