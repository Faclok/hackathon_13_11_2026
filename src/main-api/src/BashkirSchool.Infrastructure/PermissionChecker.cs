using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class PermissionChecker(CoreDbContext db) : IPermissionChecker
{
    public Task<bool> HasAsync(Guid subject, string permission, AccessScope scope, Guid? resource, CancellationToken ct) =>
        (from account in db.Users where account.Id == subject && account.Active
         from grant in db.Access where grant.SubjectId == account.Id
         join member in db.Permissions on grant.ProfileId equals member.ProfileId
         where member.Permission == permission &&
            (grant.Scope == AccessScope.Global ||
             grant.Scope == scope && (scope == AccessScope.Own ? resource == subject : grant.ResourceId == resource))
         select grant.Id).AnyAsync(ct);
}
