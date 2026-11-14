using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class CaseEvidence(CoreDbContext db)
{
    public async Task<Opinion[]> OpinionsAsync(Guid caseId, CancellationToken ct) => await (
        from r in db.Responses where r.CaseId == caseId && !r.Archived
        join j in db.Judgments on r.Id equals j.ResponseId
        select new Opinion(r.ParticipantId, j.VariantId, j.Kind, r.SubmittedAt, r.Id)).ToArrayAsync(ct);

    public async Task<ExpertCaseResponse> ViewAsync(TranslationCase translationCase, CancellationToken ct)
    {
        var fragment = await db.Fragments.AsNoTracking().SingleAsync(x => x.Id == translationCase.FragmentId, ct);
        var variants = await db.Variants.AsNoTracking().Where(x => x.CaseId == translationCase.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        var stats = ReviewStatisticsCalculator.Calculate(await OpinionsAsync(translationCase.Id, ct), variants.Select(x => x.Id));
        var resolution = translationCase.ResolutionId.HasValue
            ? await db.Resolutions.AsNoTracking().SingleAsync(x => x.Id == translationCase.ResolutionId, ct) : null;
        return new(translationCase.Id, translationCase.Revision, fragment.Text, fragment.LanguagePair,
            JsonSerializer.Deserialize<SourceReference>(fragment.SourceJson)!, translationCase.ExpertClosed, translationCase.NeedsExpert,
            resolution is null ? null : ToResponse(resolution), variants.Select(v =>
            {
                var s = stats.Variants.Single(x => x.VariantId == v.Id);
                return new VariantEvidence(v.Id, v.Text, s.Supports, s.SupportRatio);
            }).ToArray()) { FragmentId = fragment.Id };
    }

    public static ResolutionResponse ToResponse(ResolutionRevision r) => new(r.Id, r.Revision, r.Kind, r.PrimaryVariantId, r.Accepted, r.Rejected, r.CreatedAt);
}

public static class CaseWriteLock
{
    public static async Task Acquire(CoreDbContext db, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71830423)", ct);
    }
}
