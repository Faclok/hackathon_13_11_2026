using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ReviewWorkflow(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock, PlayerEconomy economy) : IReviewWorkflow
{
    public async Task<AssignmentResponse?> ReceiveAsync(Guid participant, CancellationToken ct)
        => await ReceiveAsync(participant, "translation-confirm", ct);

    public Task<AssignmentResponse?> ReceiveAsync(Guid participant, string kind, CancellationToken ct)
        => ReceiveCoreAsync(participant, kind, false, 4, ct);

    public Task<AssignmentResponse?> ReceiveBossAsync(Guid participant, CancellationToken ct)
        => ReceiveCoreAsync(participant, "boss", true, 3, ct);

    private async Task<AssignmentResponse?> ReceiveCoreAsync(Guid participant, string kind, bool randomize, int maxChoices, CancellationToken ct)
    {
        if (kind is not ("translation-confirm" or "translation-choice" or "translation-create" or "boss") ||
            kind == "boss" && !randomize) throw new DomainException("Unknown review kind.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // One small application's allocator serializes reservations; no external lock service is needed.
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71830421)", ct);
        await Require(participant, "reviews.receive", ct);
        var now = clock.GetUtcNow();
        var active = await db.Assignments.Where(x => x.ParticipantId == participant && x.Kind == kind && !x.Completed && x.ExpiresAt > now)
            .OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (active is not null) return await View(active, ct);
        var group = await (from e in db.Enrollments where e.ParticipantId == participant
                           join c in db.Classes on e.ClassId equals c.Id
                           select new { ClassId = c.Id, c.SchoolId }).SingleOrDefaultAsync(ct);
        var policy = await db.Policies.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("An administrator must configure distribution first.");
        var candidateQuery = db.Cases.Where(x => !x.ExpertClosed);
        candidateQuery = kind == "translation-create"
            ? candidateQuery.Where(c => db.Variants.Any(v => v.CaseId == c.Id && v.Text == "") &&
                !db.Variants.Any(v => v.CaseId == c.Id && v.Text != ""))
            : candidateQuery.Where(c => db.Variants.Any(v => v.CaseId == c.Id && v.Text != ""));
        var candidates = await candidateQuery.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(policy.WorkingSetSize).ToArrayAsync(ct);
        var ids = candidates.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.Where(x => ids.Contains(x.CaseId)).ToArrayAsync(ct);
        var skipped = await db.Responses.Where(x => ids.Contains(x.CaseId) && x.Action == ReviewAction.Skip)
            .Select(x => x.AssignmentId).ToArrayAsync(ct);
        var eligible = candidates.Where(c => !assignments.Any(a => a.CaseId == c.Id && a.ParticipantId == participant && a.IssuedAt > now.AddHours(-policy.RepeatHours)))
            .Select(c =>
            {
                var coverage = assignments.Where(a => a.CaseId == c.Id && (a.Completed && !skipped.Contains(a.Id) || !a.Completed && a.ExpiresAt > now))
                    .GroupBy(a => a.ParticipantId).Select(g => g.OrderByDescending(a => a.IssuedAt).First()).ToArray();
                return new { Case = c, SchoolCount = group is null ? 0 : coverage.Count(a => a.SchoolId == group.SchoolId),
                    ClassCount = group is null ? 0 : coverage.Count(a => a.ClassId == group.ClassId),
                    CoverageCount = coverage.Length,
                    AlreadyCounted = coverage.Any(a => a.ParticipantId == participant),
                    Repeats = assignments.Count(a => a.CaseId == c.Id && a.ParticipantId == participant) };
            })
            .Where(x => group is null || x.AlreadyCounted || x.SchoolCount < Math.Floor(policy.RequiredParticipants * policy.MaximumSchoolShare)
                && x.ClassCount < Math.Floor(policy.RequiredParticipants * policy.MaximumClassShare))
            .OrderBy(x => x.SchoolCount).ThenBy(x => x.ClassCount).ThenBy(x => x.Repeats)
            .ThenBy(x => group is null ? x.CoverageCount : 0)
            .ThenBy(x => randomize ? Random.Shared.Next() : 0)
            .ThenBy(x => x.Case.CreatedAt).ThenBy(x => x.Case.Id).FirstOrDefault();
        if (eligible is null) return null;
        var variants = await db.Variants.Where(x => x.CaseId == eligible.Case.Id && x.CreatedBy != participant &&
            (kind == "translation-create" ? x.Text == "" : x.Text != "")).ToArrayAsync(ct);
        var shownVariant = variants.OrderBy(v => assignments.Count(a => a.CaseId == eligible.Case.Id && a.ShownVariantId == v.Id && (a.Completed || a.ExpiresAt > now)))
            .ThenBy(v => v.CreatedAt).ThenBy(v => v.Id).FirstOrDefault();
        if (shownVariant is null) return null;
        Guid[] choices = [];
        if (kind is "translation-choice" or "boss")
        {
            var opinions = await (from r in db.Responses where r.CaseId == eligible.Case.Id && !r.Archived
                                  join j in db.Judgments on r.Id equals j.ResponseId
                                  select new Opinion(r.ParticipantId, j.VariantId, j.Kind, r.SubmittedAt, r.Id)).ToArrayAsync(ct);
            var stats = ReviewStatisticsCalculator.Calculate(opinions, variants.Select(x => x.Id));
            choices = variants.OrderByDescending(v => stats.Variants.Single(x => x.VariantId == v.Id).Supports)
                .ThenBy(v => v.CreatedAt).ThenBy(v => v.Id).Take(maxChoices).Select(v => v.Id).ToArray();
            shownVariant = variants.Single(v => v.Id == choices[0]);
        }
        var issued = ReviewAssignment.Issue(participant, eligible.Case, group?.SchoolId, group?.ClassId, policy.Id, now, TimeSpan.FromMinutes(policy.AssignmentMinutes), shownVariant.Id);
        if (kind is "translation-choice" or "boss") issued.SetChoices(choices);
        if (kind == "boss") issued.SetBoss();
        if (kind == "translation-create") issued.SetCreation();
        db.Assignments.Add(issued);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await View(issued, ct);
    }

    public async Task<ReviewReceipt> SubmitAsync(Guid participant, Guid assignmentId, SubmitReviewRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        await Require(participant, "reviews.submit", ct);
        var assignment = await db.Assignments.SingleOrDefaultAsync(x => x.Id == assignmentId && x.ParticipantId == participant, ct) ?? throw new MissingResourceException();
        var previous = await db.Responses.SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        if (previous is not null)
        {
            if (previous.Action != request.Action || previous.Correction != request.Correction || previous.SelectedVariantId != request.SelectedVariantId)
                throw new ConflictException("An assignment cannot be resubmitted with a different answer.");
            return new(previous.Id, previous.Archived);
        }
        var translationCase = await db.Cases.SingleAsync(x => x.Id == assignment.CaseId, ct);
        var shown = await db.Variants.SingleAsync(x => x.Id == assignment.ShownVariantId, ct);
        if (request.SelectedVariantId.HasValue && (request.Action != ReviewAction.Confirm || !assignment.ShownVariants.Contains(request.SelectedVariantId.Value)))
            throw new DomainException("Select one of the issued variants, without a custom correction.");
        if (assignment.Kind is ("translation-choice" or "boss") && request.Action == ReviewAction.Confirm && !request.SelectedVariantId.HasValue)
            throw new DomainException("selectedVariantId is required for translation choices.");
        if (request.Action == ReviewAction.Correct && request.Correction == shown.Text)
            throw new DomainException("Correction must differ from the shown text.");
        var profile = await db.Participants.SingleAsync(x => x.Id == participant, ct);
        var response = ReviewResponse.Submit(assignment, participant, request.Action, request.Correction, profile.Trust, clock.GetUtcNow(), translationCase.ExpertClosed);
        if (request.SelectedVariantId.HasValue) response.Select(assignment, request.SelectedVariantId.Value);
        Guid? correctionId = null;
        if (request.Action == ReviewAction.Correct)
        {
            // Exact text identity only; linguistic normalization/merging is not assumed.
            var variant = await db.Variants.FirstOrDefaultAsync(x => x.CaseId == assignment.CaseId && x.Text == request.Correction, ct);
            if (variant is null)
            {
                variant = TranslationVariant.Create(assignment.CaseId, request.Correction!, false, createdBy: participant, createdAt: clock.GetUtcNow());
                db.Variants.Add(variant);
            }
            correctionId = variant.Id;
        }
        db.Responses.Add(response);
        db.Judgments.AddRange(response.Judgments(assignment.ShownVariantId, correctionId));
        if (!response.Archived && request.Action != ReviewAction.Skip)
            db.Outbox.Add(new() { CaseId = response.CaseId, ResponseId = response.Id, CreatedAt = clock.GetUtcNow() });
        if (request.Action != ReviewAction.Skip) await economy.RewardReviewAsync(participant, assignment.Kind == "boss", ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(response.Id, response.Archived);
    }

    public async Task<CaseStatisticsResponse> StatisticsAsync(Guid participant, Guid caseId, CancellationToken ct)
    {
        await Require(participant, "reviews.submit", ct);
        if (!await db.Assignments.AnyAsync(x => x.ParticipantId == participant && x.CaseId == caseId, ct)) throw new MissingResourceException();
        var opinions = await (from r in db.Responses where r.CaseId == caseId && !r.Archived
                              join j in db.Judgments on r.Id equals j.ResponseId
                              select new Opinion(r.ParticipantId, j.VariantId, j.Kind, r.SubmittedAt, r.Id)).ToArrayAsync(ct);
        var variants = await db.Variants.Where(x => x.CaseId == caseId).Select(x => x.Id).ToArrayAsync(ct);
        var stats = ReviewStatisticsCalculator.Calculate(opinions, variants);
        var policy = await db.Policies.OrderByDescending(x => x.Version).FirstAsync(ct);
        return new(stats.UniqueParticipants, policy.RequiredParticipants,
            Math.Min(1m, (decimal)stats.UniqueParticipants / policy.RequiredParticipants), stats.Variants);
    }

    private async Task Require(Guid subject, string permission, CancellationToken ct)
    {
        if (!await permissions.HasAsync(subject, permission, AccessScope.Own, subject, ct)) throw new AccessDeniedException();
    }
    private async Task<AssignmentResponse> View(ReviewAssignment assignment, CancellationToken ct)
    {
        var translationCase = await db.Cases.AsNoTracking().SingleAsync(x => x.Id == assignment.CaseId, ct);
        var fragment = await db.Fragments.AsNoTracking().SingleAsync(x => x.Id == translationCase.FragmentId, ct);
        var variant = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == assignment.ShownVariantId, ct);
        var ids = assignment.Kind == "translation-create" ? [] : assignment.ShownVariants;
        var options = await db.Variants.Where(x => x.CaseId == assignment.CaseId && ids.Contains(x.Id)).ToArrayAsync(ct);
        var opinions = assignment.Kind is "translation-choice" or "boss" ? await (from r in db.Responses where r.CaseId == assignment.CaseId && !r.Archived
            join j in db.Judgments on r.Id equals j.ResponseId
            select new Opinion(r.ParticipantId, j.VariantId, j.Kind, r.SubmittedAt, r.Id)).ToArrayAsync(ct) : [];
        var statistics = ReviewStatisticsCalculator.Calculate(opinions, ids);
        return new(assignment.Id, assignment.CaseId, variant.Id, fragment.Text, variant.Text, assignment.ExpiresAt)
        { Kind = assignment.Kind, Options = ids.Select(id => new ReviewOptionResponse(id, options.Single(x => x.Id == id).Text)
        { Votes = (int)Math.Round((statistics.Variants.Single(x => x.VariantId == id).SupportRatio ?? 0m) * 100m) }).ToArray() };
    }
}
