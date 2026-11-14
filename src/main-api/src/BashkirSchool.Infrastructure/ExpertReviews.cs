using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ExpertReviews(CoreDbContext db, IPermissionChecker permissions, CaseEvidence evidence, TimeProvider clock)
{
    public async Task<Guid> ProposeAsync(Guid actor, Guid caseId, ProposeTranslationRequest request, CancellationToken ct)
    {
        Guard.Text(request.Text, "text");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await Require(actor, "expert.translation.resolve", caseId, ct);
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        if (c.ExpertClosed || c.Revision != request.ExpectedRevision) throw new ConflictException("Case has changed or is closed.");
        var existing = await db.Variants.SingleOrDefaultAsync(x => x.CaseId == caseId && x.Text == request.Text, ct);
        if (existing is not null) return existing.Id;
        var variant = TranslationVariant.Create(caseId, request.Text, false, createdBy: actor, createdAt: clock.GetUtcNow());
        db.Variants.Add(variant);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return variant.Id;
    }

    public async Task<IReadOnlyList<ResolutionResponse>> HistoryAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await Require(actor, "expert.case.read", caseId, ct);
        return (await db.Resolutions.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.Revision).ToArrayAsync(ct)).Select(CaseEvidence.ToResponse).ToArray();
    }
    public async Task<IReadOnlyList<ExpertQueueItem>> QueueAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new DomainException("Invalid pagination.");
        var global = await permissions.HasAsync(actor, "expert.queue.read", AccessScope.Global, null, ct);
        var grants = await (from a in db.Access where a.SubjectId == actor && a.Scope == AccessScope.Case
                            join p in db.Permissions on a.ProfileId equals p.ProfileId
                            where p.Permission == "expert.queue.read" select a.ResourceId).ToArrayAsync(ct);
        if (!global && grants.Length == 0) throw new AccessDeniedException();
        var cases = await db.Cases.AsNoTracking().Where(x => !x.ExpertClosed && (global || grants.Contains(x.Id)))
            .OrderByDescending(x => x.NeedsExpert).ThenBy(x => x.CreatedAt).ToArrayAsync(ct);
        var result = new List<ExpertQueueItem>();
        foreach (var c in cases)
        {
            var view = await evidence.ViewAsync(c, ct);
            var lead = view.Variants.OrderByDescending(x => x.SupportRatio).FirstOrDefault();
            result.Add(new(c.Id, c.Revision, c.NeedsExpert, view.Variants.Sum(x => x.Supports), lead?.SupportRatio, c.CreatedAt));
        }
        return result.OrderByDescending(x => x.Disputed).ThenBy(x => x.LeadingSupport ?? 1m).ThenBy(x => x.CreatedAt).Skip(offset).Take(limit).ToArray();
    }

    public async Task<ExpertCaseResponse> ReadAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await Require(actor, "expert.case.read", caseId, ct);
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        return await evidence.ViewAsync(c, ct);
    }

    public async Task<ResolutionResponse> ResolveAsync(Guid actor, Guid caseId, ResolveCaseRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await Require(actor, "expert.translation.resolve", caseId, ct);
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        if (c.ExpertClosed || c.Revision != request.ExpectedRevision) throw new ConflictException("Case has changed or is already closed.");
        if (request.AdditionalVariantIds is null || request.RejectedVariantIds is null || request.AdditionalVariantIds.Length > 4)
            throw new DomainException("One primary and at most four additional renderings are allowed.");
        var accepted = request.AdditionalVariantIds.Append(request.PrimaryVariantId).ToArray();
        if (accepted.Distinct().Count() != accepted.Length || request.RejectedVariantIds.Distinct().Count() != request.RejectedVariantIds.Length)
            throw new DomainException("Duplicate variants are not allowed.");
        var ids = accepted.Concat(request.RejectedVariantIds).Distinct().ToArray();
        if (await db.Variants.CountAsync(x => x.CaseId == caseId && ids.Contains(x.Id), ct) != ids.Length)
            throw new DomainException("All variants must belong to this case.");
        if (request.Comment is { Length: > 10000 }) throw new DomainException("Comment is too long.");
        var policy = await db.Policies.OrderByDescending(x => x.Version).FirstAsync(ct);
        var view = await evidence.ViewAsync(c, ct);
        var resolution = ResolutionRevision.Create(caseId, c.Revision + 1, ResolutionKind.Expert, request.PrimaryVariantId,
            accepted, request.RejectedVariantIds, policy.Id, actor, JsonSerializer.Serialize(view.Variants), request.Comment, clock.GetUtcNow());
        c.Apply(resolution);
        db.Resolutions.Add(resolution);
        db.Audit.Add(new() { ActorId = actor, TargetId = caseId, Action = "expert.resolved", At = clock.GetUtcNow() });
        // A durable recalculation also covers historical responses. Processing always reads the latest revision.
        db.Outbox.Add(new() { CaseId = caseId, ResponseId = Guid.NewGuid(), CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CaseEvidence.ToResponse(resolution);
    }

    private async Task Require(Guid actor, string permission, Guid caseId, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, permission, AccessScope.Case, caseId, ct)) throw new AccessDeniedException();
    }
}
