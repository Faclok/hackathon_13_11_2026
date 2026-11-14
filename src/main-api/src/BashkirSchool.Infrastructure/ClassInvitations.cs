using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ClassInvitations(CoreDbContext db, UserManager<Account> users, IPermissionChecker permissions,
    TimeProvider clock) : IClassInvitations
{
    public async Task<ClassInvitationResponse> InviteAsync(Guid actor, Guid classId, InviteStudentRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await RequireTeacherAsync(actor, classId, ct);
        var student = await users.FindByNameAsync(Guard.Text(request.Username, "username", 100)) ?? throw new MissingResourceException();
        await RequireIndependentAsync(student.Id, ct);
        var invitation = await db.ClassInvitations.SingleOrDefaultAsync(x => x.ClassId == classId &&
            x.ParticipantId == student.Id && x.Status == ClassInvitationStatus.Pending, ct);
        if (invitation is null)
        {
            invitation = new() { ClassId = classId, TeacherId = actor, ParticipantId = student.Id, CreatedAt = clock.GetUtcNow() };
            db.ClassInvitations.Add(invitation);
            Audit(actor, invitation.Id, "class.invitation.created");
            await db.SaveChangesAsync(ct);
        }
        var response = await ReadAsync(invitation.Id, ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    public async Task<IReadOnlyList<ClassInvitationResponse>> ForStudentAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "profile.read", AccessScope.Own, actor, ct) ||
            !await db.Participants.AnyAsync(x => x.Id == actor, ct)) throw new AccessDeniedException();
        ValidatePage(offset, limit);
        return await Query(db.ClassInvitations.Where(x => x.ParticipantId == actor).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset).Take(limit)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ClassInvitationResponse>> ForClassAsync(Guid actor, Guid classId, int offset, int limit, CancellationToken ct)
    {
        await RequireTeacherAsync(actor, classId, ct);
        ValidatePage(offset, limit);
        return await Query(db.ClassInvitations.Where(x => x.ClassId == classId && x.TeacherId == actor).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset).Take(limit)).ToArrayAsync(ct);
    }

    public async Task<ClassInvitationResponse> RespondAsync(Guid actor, Guid invitationId, RespondToClassInvitationRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var invitation = await db.ClassInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.ParticipantId == actor, ct)
            ?? throw new MissingResourceException();
        if (!await permissions.HasAsync(actor, "profile.read", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
        var decision = request.Accept ? ClassInvitationStatus.Accepted : ClassInvitationStatus.Declined;
        if (invitation.Status != ClassInvitationStatus.Pending)
        {
            if (invitation.Status != decision) throw new ConflictException("Invitation has already been resolved.");
            return await ReadAsync(invitation.Id, ct);
        }
        if (request.Accept)
        {
            await RequireTeacherAsync(invitation.TeacherId, invitation.ClassId, ct);
            await RequireIndependentAsync(actor, ct);
            db.Enrollments.Add(new() { ParticipantId = actor, ClassId = invitation.ClassId });
            // A participant can join only one class. Preserve other invitations as cancelled history.
            var others = await db.ClassInvitations.Where(x => x.ParticipantId == actor && x.Id != invitation.Id &&
                x.Status == ClassInvitationStatus.Pending).ToArrayAsync(ct);
            foreach (var other in others)
            {
                other.Status = ClassInvitationStatus.Cancelled;
                other.RespondedAt = clock.GetUtcNow();
                Audit(actor, other.Id, "class.invitation.cancelled");
            }
        }
        invitation.Status = decision;
        invitation.RespondedAt = clock.GetUtcNow();
        Audit(actor, invitation.Id, request.Accept ? "class.invitation.accepted" : "class.invitation.declined");
        await db.SaveChangesAsync(ct);
        var response = await ReadAsync(invitation.Id, ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    private async Task RequireTeacherAsync(Guid actor, Guid classId, CancellationToken ct)
    {
        var group = await db.Classes.SingleOrDefaultAsync(x => x.Id == classId && x.TeacherId == actor, ct) ?? throw new AccessDeniedException();
        if (!await permissions.HasAsync(actor, "students.create", AccessScope.School, group.SchoolId, ct)) throw new AccessDeniedException();
    }

    private async Task RequireIndependentAsync(Guid participantId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == participantId && x.Active && x.SelfRegistered, ct) ||
            !await db.Participants.AnyAsync(x => x.Id == participantId, ct) ||
            await db.Access.AnyAsync(x => x.SubjectId == participantId && x.ProfileId != "account" && x.ProfileId != "participant", ct))
            throw new AccessDeniedException();
        if (await db.Enrollments.AnyAsync(x => x.ParticipantId == participantId, ct))
            throw new ConflictException("Participant already belongs to a class.");
    }

    private IQueryable<ClassInvitationResponse> Query(IQueryable<ClassInvitation> source) =>
        from invitation in source.AsNoTracking()
        join studyClass in db.Classes on invitation.ClassId equals studyClass.Id
        join school in db.Schools on studyClass.SchoolId equals school.Id
        join teacher in db.Users on invitation.TeacherId equals teacher.Id
        join student in db.Users on invitation.ParticipantId equals student.Id
        select new ClassInvitationResponse(invitation.Id, studyClass.Id, studyClass.Name, school.Id, school.Name,
            teacher.Id, teacher.UserName!, student.Id, student.UserName!, invitation.Status, invitation.CreatedAt, invitation.RespondedAt);

    private Task<ClassInvitationResponse> ReadAsync(Guid id, CancellationToken ct) => Query(db.ClassInvitations.Where(x => x.Id == id)).SingleAsync(ct);
    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 100) throw new DomainException("Use offset >= 0 and limit between 1 and 100.");
    }
    private void Audit(Guid actor, Guid target, string action) =>
        db.Audit.Add(new() { ActorId = actor, TargetId = target, Action = action, At = clock.GetUtcNow() });
}
