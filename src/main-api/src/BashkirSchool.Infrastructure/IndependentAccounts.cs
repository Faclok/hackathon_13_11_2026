using System.Text;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class IndependentAccounts(CoreDbContext db, UserManager<Account> users, IPermissionChecker permissions, TimeProvider clock)
{
    public async Task<AccountResponse> RegisterAsync(RegisterAccountRequest request, CancellationToken ct)
    {
        Guard.Text(request.Username, "username", 100); Guard.Text(request.Password, "password", 1000);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await users.FindByNameAsync(request.Username) is not null) throw new ConflictException("Username is already in use.");
        var account = new Account { Id = Guid.NewGuid(), UserName = request.Username, SelfRegistered = true };
        Check(await users.CreateAsync(account, request.Password));
        db.Participants.Add(new() { Id = account.Id });
        db.Access.AddRange(new AccessAssignment { SubjectId = account.Id, ProfileId = "account", Scope = AccessScope.Own },
            new AccessAssignment { SubjectId = account.Id, ProfileId = "participant", Scope = AccessScope.Own });
        db.Audit.Add(new() { ActorId = account.Id, TargetId = account.Id, Action = "account.self.registered", At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(account.Id, account.UserName!);
    }

    public async Task<MyProfileResponse> ProfileAsync(Guid actor, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "profile.read", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        var group = await (from e in db.Enrollments where e.ParticipantId == actor
                           join c in db.Classes on e.ClassId equals c.Id select new { ClassId = c.Id, c.SchoolId }).SingleOrDefaultAsync(ct);
        return new(account.Id, account.UserName!, group?.SchoolId, group?.ClassId);
    }

    public async Task<PasswordSetupResponse> IssueRecoveryAsync(Guid actor, Guid accountId, RecoverIndependentAccountRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await permissions.HasAsync(actor, "accounts.credentials.reset", AccessScope.Global, null, ct)) throw new AccessDeniedException();
        var note = Guard.Text(request.VerificationNote, "verificationNote", 2000);
        var account = await users.FindByIdAsync(accountId.ToString()) ?? throw new MissingResourceException();
        if (!account.Active || !account.SelfRegistered || !await db.Participants.AnyAsync(x => x.Id == accountId, ct) ||
            await db.Enrollments.AnyAsync(x => x.ParticipantId == accountId, ct) ||
            await db.Access.AnyAsync(x => x.SubjectId == accountId && x.ProfileId != "account" && x.ProfileId != "participant", ct))
            throw new AccessDeniedException();
        Check(await users.UpdateSecurityStampAsync(account));
        var token = await users.GeneratePasswordResetTokenAsync(account);
        db.Audit.Add(new() { ActorId = actor, TargetId = accountId, Action = "independent.password.link.issued", Reason = note, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(accountId, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)), clock.GetUtcNow().AddHours(24));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new DomainException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
