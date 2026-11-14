using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class SchoolDirectory(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock)
{
    public async Task<IReadOnlyList<AccessAssignmentResponse>> AccessAsync(Guid actor, Guid accountId, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "access.assign", AccessScope.Global, null, ct)) throw new AccessDeniedException();
        return await db.Access.Where(x => x.SubjectId == accountId).OrderBy(x => x.ProfileId).ThenBy(x => x.Id)
            .Select(x => new AccessAssignmentResponse(x.Id, x.ProfileId, x.Scope, x.ResourceId)).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<SchoolResponse>> SchoolsAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        ValidatePage(offset, limit);
        var global = await permissions.HasAsync(actor, "schools.manage", AccessScope.Global, null, ct);
        var schoolIds = await TeacherSchools(actor, "classes.read", ct);
        if (!global && schoolIds.Length == 0) throw new AccessDeniedException();
        return await db.Schools.AsNoTracking().Where(x => global || schoolIds.Contains(x.Id)).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(offset).Take(limit).Select(x => new SchoolResponse(x.Id, x.Name)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ClassResponse>> ClassesAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        ValidatePage(offset, limit);
        var global = await permissions.HasAsync(actor, "classes.manage", AccessScope.Global, null, ct);
        var schoolIds = await TeacherSchools(actor, "classes.read", ct);
        if (!global && schoolIds.Length == 0) throw new AccessDeniedException();
        return await db.Classes.AsNoTracking().Where(x => global || x.TeacherId == actor && schoolIds.Contains(x.SchoolId))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(limit).Select(x => new ClassResponse(x.Id, x.SchoolId, x.Name)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<AccountResponse>> StudentsAsync(Guid actor, Guid classId, int offset, int limit, CancellationToken ct)
    {
        ValidatePage(offset, limit);
        var c = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct) ?? throw new MissingResourceException();
        var global = await permissions.HasAsync(actor, "enrollments.manage", AccessScope.Global, null, ct);
        if (!global && (c.TeacherId != actor || !await permissions.HasAsync(actor, "students.read", AccessScope.School, c.SchoolId, ct)))
            throw new AccessDeniedException();
        return await (from e in db.Enrollments where e.ClassId == classId
                      join u in db.Users on e.ParticipantId equals u.Id
                      orderby u.UserName, u.Id select new AccountResponse(u.Id, u.UserName!)
                      { FirstName = u.FirstName, LastName = u.LastName }).Skip(offset).Take(limit).ToArrayAsync(ct);
    }

    public async Task AssignAsync(Guid actor, Guid accountId, AssignAccessRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await permissions.HasAsync(actor, "access.assign", AccessScope.Global, null, ct)) throw new AccessDeniedException();
        var permission = request.Profile switch
        {
            "teacher" => "teachers.manage", "expert" => "experts.manage", "producer" or "exporter" => "access.assign",
            _ => throw new DomainException("This operation can assign only teacher, expert, producer or exporter capabilities.")
        };
        if (!await permissions.HasAsync(actor, permission, AccessScope.Global, null, ct)) throw new AccessDeniedException();
        if (!await db.Users.AnyAsync(x => x.Id == accountId, ct)) throw new MissingResourceException();
        if (request.Profile == "teacher")
        {
            if (!request.SchoolId.HasValue || request.CaseId.HasValue || !await db.Schools.AnyAsync(x => x.Id == request.SchoolId, ct))
                throw new DomainException("A teacher needs an existing school.");
        }
        else if (request.SchoolId.HasValue) throw new DomainException("School scope is only supported for teachers.");
        if (request.CaseId.HasValue && (request.Profile is not ("expert" or "exporter") || !await db.Cases.AnyAsync(x => x.Id == request.CaseId, ct)))
            throw new DomainException("Invalid case scope.");
        var scope = request.Profile == "teacher" ? AccessScope.School : request.CaseId.HasValue ? AccessScope.Case : request.Profile == "producer" ? AccessScope.Own : AccessScope.Global;
        var resource = request.SchoolId ?? request.CaseId;
        if (!await db.Access.AnyAsync(x => x.SubjectId == accountId && x.ProfileId == request.Profile && x.Scope == scope && x.ResourceId == resource, ct))
        {
            db.Access.Add(new() { SubjectId = accountId, ProfileId = request.Profile, Scope = scope, ResourceId = resource });
            db.Audit.Add(new() { ActorId = actor, TargetId = accountId, Action = "access.assigned." + request.Profile, At = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task RevokeAsync(Guid actor, Guid assignmentId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await permissions.HasAsync(actor, "access.assign", AccessScope.Global, null, ct)) throw new AccessDeniedException();
        var assignment = await db.Access.SingleOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new MissingResourceException();
        if (assignment.ProfileId is "administrator" or "account") throw new DomainException("Base account and administrator assignments cannot be revoked through this operation.");
        db.Access.Remove(assignment);
        db.Audit.Add(new() { ActorId = actor, TargetId = assignment.SubjectId, Action = "access.revoked." + assignment.ProfileId, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Guid[]> TeacherSchools(Guid actor, string permission, CancellationToken ct) => await (
        from a in db.Access where a.SubjectId == actor && a.Scope == AccessScope.School && a.ResourceId != null
        join u in db.Users on a.SubjectId equals u.Id where u.Active
        join p in db.Permissions on a.ProfileId equals p.ProfileId where p.Permission == permission
        select a.ResourceId!.Value).Distinct().ToArrayAsync(ct);
    private static void ValidatePage(int offset, int limit)
    { if (offset < 0 || limit is < 1 or > 100) throw new DomainException("Invalid pagination."); }
}
