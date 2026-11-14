using System.Text.Json;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BashkirSchool.Infrastructure;

public sealed class ResolutionProcessor(CoreDbContext db, TimeProvider clock)
{
    public async Task ProcessAsync(Guid caseId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var c = await db.Cases.SingleAsync(x => x.Id == caseId, ct);
        var resolution = c.ResolutionId.HasValue ? await db.Resolutions.SingleAsync(x => x.Id == c.ResolutionId, ct) : null;
        if (!c.ExpertClosed)
        {
            var variants = await db.Variants.Where(x => x.CaseId == caseId && x.Text != "").ToArrayAsync(ct);
            var observationRows = await (from r in db.Responses where r.CaseId == caseId && !r.Archived
                                      join a in db.Assignments on r.AssignmentId equals a.Id
                                      join j in db.Judgments on r.Id equals j.ResponseId where j.Kind == JudgmentKind.Support
                                      select new { r.ParticipantId, j.VariantId, a.SchoolId, a.ClassId, r.SubmittedAt, r.Id, a.ShownVariantId, a.ShownVariantsJson }).ToArrayAsync(ct);
            var observations = observationRows.Select(r => new ReviewObservation(r.ParticipantId, r.VariantId, r.SchoolId, r.ClassId, r.SubmittedAt, r.Id, r.ShownVariantId)
            { ShownVariantIds = r.ShownVariantsJson == "[]" ? [r.ShownVariantId] : JsonSerializer.Deserialize<Guid[]>(r.ShownVariantsJson) }).ToArray();
            var policy = await db.Policies.OrderByDescending(x => x.Version).FirstAsync(ct);
            var support = ConsensusCalculator.Calculate(observations, variants, policy);
            var eligible = support.Where(x => x.Qualified).OrderByDescending(x => x.Ratio).ToArray();
            var uniqueWinner = eligible.Length > 0 && (eligible.Length == 1 || eligible[0].Ratio > eligible[1].Ratio);
            var disputed = !uniqueWinner && support.Any(x => x.IndependentObservations >= policy.RequiredParticipants);
            var teacherRequested = await db.Audit.AnyAsync(x => x.TargetId == caseId && x.Action == "lms.expert.requested", ct);
            c.SetDisputed(disputed || teacherRequested);
            var primary = uniqueWinner ? (Guid?)eligible[0].VariantId : null;
            if (resolution?.PrimaryVariantId != primary && (primary.HasValue || resolution is { Kind: ResolutionKind.Community }))
            {
                resolution = ResolutionRevision.Create(caseId, c.Revision + 1, primary.HasValue ? ResolutionKind.Community : ResolutionKind.Pending,
                    primary, primary.HasValue ? [primary.Value] : [], [], policy.Id, null, JsonSerializer.Serialize(support), null, clock.GetUtcNow());
                c.Apply(resolution);
                c.SetDisputed(disputed || teacherRequested);
                db.Resolutions.Add(resolution);
                await db.SaveChangesAsync(ct);
            }
        }
        // Losing a definite result retains prior contributions until a new definite decision.
        if (resolution is { Kind: not ResolutionKind.Pending }) await AssessAsync(caseId, resolution, ct);
        await db.SaveChangesAsync(ct);
        await db.Outbox.Where(x => x.CaseId == caseId && x.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessedAt, clock.GetUtcNow()), ct);
        await transaction.CommitAsync(ct);
    }

    private async Task AssessAsync(Guid caseId, ResolutionRevision resolution, CancellationToken ct)
    {
        var claims = await (from r in db.Responses where r.CaseId == caseId && !r.Archived
                            join j in db.Judgments on r.Id equals j.ResponseId where j.Kind == JudgmentKind.Support
                            join a in db.Assignments on r.AssignmentId equals a.Id
                            select new { Judgment = j, r.ParticipantId,
                                IsBoss = a.Kind == "boss-teacher" || db.Set<LevelBossQuestion>().Any(x => x.AssignmentId == a.Id) }).ToArrayAsync(ct);
        var accepted = resolution.Accepted.ToHashSet();
        var rejected = resolution.Rejected.ToHashSet();
        foreach (var claim in claims)
        {
            var prior = await db.TrustContributions.SingleOrDefaultAsync(x => x.JudgmentId == claim.Judgment.Id, ct);
            if (prior is not null && (prior.Revision > resolution.Revision || prior.ResolutionId == resolution.Id)) continue;
            var outcome = accepted.Contains(claim.Judgment.VariantId) ? AssessmentOutcome.Correct : rejected.Contains(claim.Judgment.VariantId) ? AssessmentOutcome.Incorrect : AssessmentOutcome.Pending;
            var correct = outcome == AssessmentOutcome.Correct ? 1 : 0;
            var total = outcome == AssessmentOutcome.Pending ? 0 : 1;
            var deltaCorrect = correct - (prior?.Correct ?? 0);
            var deltaTotal = total - (prior?.Total ?? 0);
            if (deltaCorrect != 0 && claim.IsBoss)
                await db.Set<PlayerState>().Where(x => x.ParticipantId == claim.ParticipantId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Coins, x => x.Coins + 4 * deltaCorrect), ct);
            await db.Participants.Where(x => x.Id == claim.ParticipantId).ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.Correct, x => x.Correct + deltaCorrect).SetProperty(x => x.Total, x => x.Total + deltaTotal), ct);
            if (prior is null)
            {
                prior = new() { JudgmentId = claim.Judgment.Id, ParticipantId = claim.ParticipantId };
                db.TrustContributions.Add(prior);
            }
            prior.ResolutionId = resolution.Id; prior.Revision = resolution.Revision; prior.Correct = correct; prior.Total = total;
            db.TrustAssessments.Add(new() { JudgmentId = claim.Judgment.Id, ResolutionId = resolution.Id, Outcome = outcome, AssessedAt = clock.GetUtcNow() });
        }
    }
}

public sealed class ResolutionWorker(IServiceScopeFactory scopes, ILogger<ResolutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid[] cases;
                using (var scope = scopes.CreateScope())
                    cases = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Outbox.Where(x => x.ProcessedAt == null)
                        .OrderBy(x => x.CreatedAt).Select(x => x.CaseId).Take(32).Distinct().ToArrayAsync(stoppingToken);
                foreach (var caseId in cases)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(caseId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception e) { logger.LogWarning(e, "Case {CaseId} will retry; other queued cases can continue.", caseId); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning(e, "Resolution processing will retry; queued work has been retained."); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
