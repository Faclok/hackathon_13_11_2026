using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BashkirSchool.Infrastructure;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Configure Jwt:Issuer, Jwt:Audience and a secret Jwt:SigningKey of at least 32 UTF-8 bytes.");
    }
}

public sealed class SchoolAccounts(CoreDbContext db, UserManager<Account> users, IPermissionChecker permissions,
    TimeProvider clock, Sessions sessions)
{
    public async Task ChangePasswordAsync(Guid actor, ChangePasswordRequest request, CancellationToken ct)
    {
        Guard.Text(request.CurrentPassword, "currentPassword", 1000); Guard.Text(request.NewPassword, "newPassword", 1000);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "credentials.change", AccessScope.Own, actor, ct);
        var account = await users.FindByIdAsync(actor.ToString()) ?? throw new AccessDeniedException();
        Check(await users.ChangePasswordAsync(account, request.CurrentPassword, request.NewPassword));
        account.SessionVersion++;
        Check(await users.UpdateAsync(account));
        Audit(actor, actor, "password.changed");
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<AccessTokenResponse> SignInAsync(SignInRequest request, CancellationToken ct)
    {
        Guard.Text(request.Password, "password", 1000);
        var account = await users.FindByNameAsync(Guard.Text(request.Username, "username", 100));
        if (account is null || !account.Active || await users.IsLockedOutAsync(account)) throw new AccessDeniedException();
        if (!await users.CheckPasswordAsync(account, request.Password))
        {
            Check(await users.AccessFailedAsync(account));
            throw new AccessDeniedException();
        }
        Check(await users.ResetAccessFailedCountAsync(account));
        return await sessions.OpenAsync(account, ct);
    }

    public async Task<Guid> CreateSchoolAsync(Guid actor, CreateSchoolRequest request, CancellationToken ct)
    {
        await Require(actor, "schools.manage", AccessScope.Global, null, ct);
        var school = new School { Name = Guard.Text(request.Name, "name", 200) };
        db.Schools.Add(school);
        Audit(actor, school.Id, "school.created");
        await db.SaveChangesAsync(ct);
        return school.Id;
    }

    public async Task<AccountResponse> CreateStaffAsync(Guid actor, CreateStaffRequest request, CancellationToken ct)
    {
        var permission = request.Profile switch { "teacher" => "teachers.manage", "expert" => "experts.manage", "producer" or "exporter" => "access.assign", _ => throw new DomainException("Unsupported staff profile.") };
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, permission, AccessScope.Global, null, ct);
        if (request.Profile == "teacher" && (!request.SchoolId.HasValue || !await db.Schools.AnyAsync(x => x.Id == request.SchoolId, ct)))
            throw new DomainException("A teacher needs an existing school.");
        if (request.Profile != "teacher" && request.SchoolId.HasValue) throw new DomainException("School scope is only supported for teachers.");
        var account = await CreateAccount(request.Username);
        db.Access.Add(new() { SubjectId = account.Id, ProfileId = request.Profile,
            Scope = request.Profile == "teacher" ? AccessScope.School : request.Profile == "producer" ? AccessScope.Own : AccessScope.Global,
            ResourceId = request.SchoolId });
        Audit(actor, account.Id, "account.created." + request.Profile);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(account.Id, account.UserName!);
    }

    public async Task<Guid> CreateClassAsync(Guid actor, CreateClassRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "classes.create", AccessScope.School, request.SchoolId, ct);
        var studyClass = new StudyClass { SchoolId = request.SchoolId, TeacherId = actor, Name = Guard.Text(request.Name, "name", 200) };
        db.Classes.Add(studyClass);
        Audit(actor, studyClass.Id, "class.created");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return studyClass.Id;
    }

    public async Task<AccountResponse> CreateStudentAsync(Guid actor, Guid classId, CreateStudentRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await RequireTeacherClass(actor, classId, "students.create", ct);
        var existing = await users.FindByNameAsync(Guard.Text(request.Username, "username", 100));
        if (existing is not null)
        {
            if (!await db.Enrollments.AnyAsync(x => x.ParticipantId == existing.Id && x.ClassId == classId, ct))
                throw new ConflictException("Username is already in use.");
            return new(existing.Id, existing.UserName!);
        }
        var account = await CreateAccount(request.Username);
        db.Participants.Add(new() { Id = account.Id });
        db.Enrollments.Add(new() { ParticipantId = account.Id, ClassId = classId });
        db.Access.Add(new() { SubjectId = account.Id, ProfileId = "participant", Scope = AccessScope.Own });
        Audit(actor, account.Id, "student.created");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(account.Id, account.UserName!);
    }

    public async Task<PasswordSetupResponse> IssuePasswordAsync(Guid actor, Guid accountId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var account = await users.FindByIdAsync(accountId.ToString()) ?? throw new MissingResourceException();
        if (!await permissions.HasAsync(actor, "teachers.manage", AccessScope.Global, null, ct))
        {
            var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.ParticipantId == accountId, ct) ?? throw new AccessDeniedException();
            await RequireTeacherClass(actor, enrollment.ClassId, account.PasswordHash is null ? "students.credentials.issue" : "students.credentials.reset", ct);
        }
        else
        {
            // Administrator may bootstrap staff, but may not reset another administrator through this operation.
            if (!await db.Access.AnyAsync(x => x.SubjectId == accountId && (x.ProfileId == "teacher" || x.ProfileId == "expert" || x.ProfileId == "producer" || x.ProfileId == "exporter"), ct))
                throw new AccessDeniedException();
        }
        Check(await users.UpdateSecurityStampAsync(account));
        var token = await users.GeneratePasswordResetTokenAsync(account);
        Audit(actor, accountId, "password.link.issued");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(accountId, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)), clock.GetUtcNow().AddHours(24));
    }

    public async Task CompletePasswordAsync(CompletePasswordRequest request, CancellationToken ct)
    {
        Guard.Text(request.Password, "password", 1000);
        Guard.Text(request.Token, "token", 10000);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var account = await users.FindByIdAsync(request.AccountId.ToString()) ?? throw new AccessDeniedException();
        if (!account.Active) throw new AccessDeniedException();
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (FormatException) { throw new DomainException("Invalid password token."); }
        Check(await users.ResetPasswordAsync(account, token, request.Password));
        account.SessionVersion++;
        account.LockoutEnd = null;
        account.AccessFailedCount = 0;
        Check(await users.UpdateAsync(account));
        Audit(account.Id, account.Id, "password.changed");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetActiveAsync(Guid actor, Guid accountId, bool active, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "accounts.status.manage", AccessScope.Global, null, ct);
        var account = await users.FindByIdAsync(accountId.ToString()) ?? throw new MissingResourceException();
        account.Active = active;
        account.SessionVersion++;
        Check(await users.UpdateAsync(account));
        Audit(actor, accountId, active ? "account.unblocked" : "account.blocked");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Guid> SavePolicyAsync(Guid actor, DistributionPolicy policy, CancellationToken ct)
    {
        policy.Validate();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "policies.distribution.manage", AccessScope.Global, null, ct);
        await Require(actor, "policies.consensus.manage", AccessScope.Global, null, ct);
        policy.Id = Guid.NewGuid();
        policy.Version = (await db.Policies.MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        policy.ChangedBy = actor;
        policy.CreatedAt = clock.GetUtcNow();
        db.Policies.Add(policy);
        Audit(actor, policy.Id, "policy.created");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return policy.Id;
    }

    public async Task BootstrapAsync(string username, string password, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await db.Users.AnyAsync(ct)) throw new ConflictException("Bootstrap is only available for an empty account store.");
        var account = new Account { Id = Guid.NewGuid(), UserName = Guard.Text(username, "username", 100) };
        Check(await users.CreateAsync(account, password));
        db.Access.Add(new() { SubjectId = account.Id, ProfileId = "administrator", Scope = AccessScope.Global });
        db.Access.Add(new() { SubjectId = account.Id, ProfileId = "account", Scope = AccessScope.Own });
        db.Policies.Add(new() { Version = 1, ChangedBy = account.Id, CreatedAt = clock.GetUtcNow() });
        Audit(account.Id, account.Id, "administrator.bootstrapped");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Account> CreateAccount(string username)
    {
        var account = new Account { Id = Guid.NewGuid(), UserName = Guard.Text(username, "username", 100) };
        Check(await users.CreateAsync(account));
        db.Access.Add(new() { SubjectId = account.Id, ProfileId = "account", Scope = AccessScope.Own });
        return account;
    }
    private async Task RequireTeacherClass(Guid actor, Guid classId, string permission, CancellationToken ct)
    {
        var studyClass = await db.Classes.SingleOrDefaultAsync(x => x.Id == classId && x.TeacherId == actor, ct) ?? throw new AccessDeniedException();
        await Require(actor, permission, AccessScope.School, studyClass.SchoolId, ct);
    }
    private async Task Require(Guid actor, string permission, AccessScope scope, Guid? resource, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, permission, scope, resource, ct)) throw new AccessDeniedException();
    }
    private void Audit(Guid actor, Guid target, string action) => db.Audit.Add(new() { ActorId = actor, TargetId = target, Action = action, At = clock.GetUtcNow() });
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new DomainException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
