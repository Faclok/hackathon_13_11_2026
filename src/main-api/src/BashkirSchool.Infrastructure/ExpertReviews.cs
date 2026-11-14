using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ExpertReviews(CoreDbContext db, CaseEvidence evidence, TimeProvider clock)
{
    public async Task<Guid> ProposeAsync(Guid actor, Guid caseId, ProposeTranslationRequest request, CancellationToken ct)
    {
        Guard.Text(request.Text, "text");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await RequireExpert(actor, ct);
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        if (c.ExpertClosed || c.Revision != request.ExpectedRevision) throw new ConflictException("Case has changed or is closed.");
        await EnsureNotClaimedByAnother(actor, caseId, ct);
        var existing = await db.Variants.SingleOrDefaultAsync(x => x.CaseId == caseId && x.Text == request.Text, ct);
        if (existing is not null) return existing.Id;
        var variant = TranslationVariant.Create(caseId, request.Text, false, createdBy: actor, createdAt: clock.GetUtcNow());
        db.Variants.Add(variant);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return variant.Id;
    }

    public async Task<IReadOnlyList<ResolutionResponse>> HistoryAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        return (await db.Resolutions.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.Revision).ToArrayAsync(ct)).Select(CaseEvidence.ToResponse).ToArray();
    }
    public async Task<IReadOnlyList<ExpertQueueItem>> QueueAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new DomainException("Invalid pagination.");
        return (await QueueCandidates(actor, ct)).Skip(offset).Take(limit).ToArray();
    }

    private async Task<IReadOnlyList<ExpertQueueItem>> QueueCandidates(Guid actor, CancellationToken ct)
    {
        var cases = await AccessibleCases(actor, ct);
        var preferences = await PreferencesAsync(actor, ct);
        var result = new List<ExpertQueueItem>();
        foreach (var c in cases)
        {
            var view = await evidence.ViewAsync(c, ct);
            var observations = view.Variants.Sum(x => x.Supports);
            var lead = view.Variants.Max(x => x.SupportRatio) ?? 0m;
            var teacherRequested = await db.Audit.AnyAsync(x => x.TargetId == c.Id && x.Action == "lms.expert.requested", ct);
            if (!Eligible(c, observations, lead, preferences, teacherRequested) || await ActiveClaim(c.Id, ct) is not null) continue;
            result.Add(new(c.Id, c.Revision, view.Original, true, observations, lead, c.CreatedAt, teacherRequested));
        }
        return result.OrderByDescending(x => x.TeacherRequested).ThenBy(x => x.LeadingSupport).ThenBy(x => x.CreatedAt).ToArray();
    }

    public async Task<ExpertDashboardResponse> DashboardAsync(Guid actor, CancellationToken ct)
    {
        var available = await QueueCandidates(actor, ct);
        var work = await WorkAsync(actor, ct);
        var preferences = await PreferencesAsync(actor, ct);
        var totalCompleted = await db.Cases.CountAsync(x => x.ExpertClosed, ct);
        return new(available.Count, work.Count(x => !x.Completed), work.Count(x => x.Completed),
            preferences.RequiredParticipants, preferences.SupportThreshold, totalCompleted);
    }

    public async Task<IReadOnlyList<ExpertWorkItem>> WorkAsync(Guid actor, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        var claims = await db.Audit.AsNoTracking().Where(x => x.ActorId == actor && x.Action == "expert.claimed")
            .OrderByDescending(x => x.At).ToArrayAsync(ct);
        var result = new List<ExpertWorkItem>();
        foreach (var claim in claims.DistinctBy(x => x.TargetId))
        {
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.TargetId, ct);
            if (c is null) continue;
            var resolution = await db.Resolutions.AsNoTracking().Where(x => x.CaseId == c.Id && x.Kind == ResolutionKind.Expert)
                .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
            if (resolution is null && (await ActiveClaim(c.Id, ct))?.ActorId != actor) continue;
            if (resolution is not null && resolution.DecidedBy != actor) continue;
            var view = await evidence.ViewAsync(c, ct);
            result.Add(new(c.Id, c.Revision, view.Original, view.Variants.Sum(x => x.Supports),
                view.Variants.Max(x => x.SupportRatio), claim.At, resolution is not null, resolution?.CreatedAt));
        }
        // Existing expert decisions created before reservations were introduced remain visible.
        var historical = await db.Resolutions.AsNoTracking().Where(x => x.Kind == ResolutionKind.Expert && x.DecidedBy == actor)
            .OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
        foreach (var resolution in historical.DistinctBy(x => x.CaseId).Where(x => result.All(item => item.CaseId != x.CaseId)))
        {
            var c = await db.Cases.AsNoTracking().SingleAsync(x => x.Id == resolution.CaseId, ct);
            var view = await evidence.ViewAsync(c, ct);
            result.Add(new(c.Id, c.Revision, view.Original, view.Variants.Sum(x => x.Supports),
                view.Variants.Max(x => x.SupportRatio), resolution.CreatedAt, true, resolution.CreatedAt));
        }
        return result.OrderBy(x => x.Completed).ThenByDescending(x => x.TakenAt).ToArray();
    }

    public async Task<ExpertWorkItem> ClaimAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await RequireExpert(actor, ct);
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        var view = await evidence.ViewAsync(c, ct);
        var count = view.Variants.Sum(x => x.Supports);
        var lead = view.Variants.Max(x => x.SupportRatio) ?? 0m;
        var teacherRequested = await db.Audit.AnyAsync(x => x.TargetId == caseId && x.Action == "lms.expert.requested", ct);
        if (!Eligible(c, count, lead, await PreferencesAsync(actor, ct), teacherRequested)) throw new ConflictException("Case does not need expert review.");
        var current = await ActiveClaim(caseId, ct);
        if (current is not null && current.ActorId != actor) throw new ConflictException("Case is already taken by another expert.");
        if (current is null)
        {
            current = new() { ActorId = actor, TargetId = caseId, Action = "expert.claimed", At = await NextClaimTime(caseId, ct) };
            db.Audit.Add(current);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(c.Id, c.Revision, view.Original, count, lead, current.At, false, null);
    }

    public async Task ReleaseAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await RequireExpert(actor, ct);
        if ((await ActiveClaim(caseId, ct))?.ActorId != actor) throw new ConflictException("Case is not taken by this expert.");
        if (await db.Cases.AnyAsync(x => x.Id == caseId && x.ExpertClosed, ct)) throw new ConflictException("Case is already closed.");
        db.Audit.Add(new() { ActorId = actor, TargetId = caseId, Action = "expert.released", At = await NextClaimTime(caseId, ct) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<ExpertCaseResponse> ReadAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        var note = await db.Audit.AsNoTracking().Where(x => x.TargetId == caseId && x.Action == "lms.expert.requested")
            .OrderByDescending(x => x.At).Select(x => x.Reason).FirstOrDefaultAsync(ct);
        return (await evidence.ViewAsync(c, ct)) with { TeacherNote = note };
    }

    public async Task<ResolutionResponse> ResolveAsync(Guid actor, Guid caseId, ResolveCaseRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await RequireExpert(actor, ct);
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new MissingResourceException();
        if (c.ExpertClosed || c.Revision != request.ExpectedRevision) throw new ConflictException("Case has changed or is already closed.");
        var claim = await EnsureNotClaimedByAnother(actor, caseId, ct);
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
        if (claim is null) db.Audit.Add(new() { ActorId = actor, TargetId = caseId, Action = "expert.claimed", At = await NextClaimTime(caseId, ct) });
        db.Audit.Add(new() { ActorId = actor, TargetId = caseId, Action = "expert.resolved", At = clock.GetUtcNow() });
        // A durable recalculation also covers historical responses. Processing always reads the latest revision.
        db.Outbox.Add(new() { CaseId = caseId, ResponseId = Guid.NewGuid(), CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CaseEvidence.ToResponse(resolution);
    }

    private async Task RequireExpert(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.Active, ct)) throw new AccessDeniedException();
    }

    private async Task<TranslationCase[]> AccessibleCases(Guid actor, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        return await db.Cases.AsNoTracking().Where(x => !x.ExpertClosed)
            .OrderBy(x => x.CreatedAt).ToArrayAsync(ct);
    }

    private Task<DistributionPolicy> CurrentPolicy(CancellationToken ct) =>
        db.Policies.AsNoTracking().OrderByDescending(x => x.Version).FirstAsync(ct);

    public async Task<ExpertPreferencesResponse> PreferencesAsync(Guid actor, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        return new(account.ExpertRequiredParticipants, account.ExpertSupportThreshold);
    }

    public async Task<ExpertPreferencesResponse> SavePreferencesAsync(Guid actor, ExpertPreferencesResponse preferences, CancellationToken ct)
    {
        await RequireExpert(actor, ct);
        if (preferences.RequiredParticipants is < 1 or > 100000 || preferences.SupportThreshold is <= 0 or > 1)
            throw new DomainException("Invalid expert queue settings.");
        var account = await db.Users.SingleAsync(x => x.Id == actor, ct);
        account.ExpertRequiredParticipants = preferences.RequiredParticipants;
        account.ExpertSupportThreshold = preferences.SupportThreshold;
        await db.SaveChangesAsync(ct);
        return preferences;
    }

    private static bool Eligible(TranslationCase c, int observations, decimal lead, ExpertPreferencesResponse preferences, bool teacherRequested) =>
        !c.ExpertClosed && (teacherRequested || observations >= preferences.RequiredParticipants && lead < preferences.SupportThreshold);

    private async Task<AuditEntry?> LastClaimEvent(Guid caseId, CancellationToken ct)
    {
        return await db.Audit.AsNoTracking().Where(x => x.TargetId == caseId &&
            (x.Action == "expert.claimed" || x.Action == "expert.released"))
            .OrderByDescending(x => x.At).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<AuditEntry?> ActiveClaim(Guid caseId, CancellationToken ct)
    {
        var last = await LastClaimEvent(caseId, ct);
        return last?.Action == "expert.claimed" ? last : null;
    }

    private async Task<DateTimeOffset> NextClaimTime(Guid caseId, CancellationToken ct)
    {
        var last = await LastClaimEvent(caseId, ct);
        var now = clock.GetUtcNow();
        return last is not null && last.At >= now ? last.At.AddTicks(1) : now;
    }

    private async Task<AuditEntry?> EnsureNotClaimedByAnother(Guid actor, Guid caseId, CancellationToken ct)
    {
        var claim = await ActiveClaim(caseId, ct);
        if (claim is not null && claim.ActorId != actor) throw new ConflictException("Case is taken by another expert.");
        return claim;
    }
}
