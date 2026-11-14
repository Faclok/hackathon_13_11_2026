using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class TranslationExport(CoreDbContext db, IPermissionChecker permissions, CaseEvidence evidence)
{
    public async Task<IReadOnlyList<ExportRow>> ReadAsync(Guid actor, int offset, int limit, bool includeDisputed, CancellationToken ct)
    {
        if (offset < 0 || limit is < 1 or > 1000) throw new DomainException("Invalid pagination.");
        await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsNpgsql()
            ? System.Data.IsolationLevel.RepeatableRead : System.Data.IsolationLevel.Serializable, ct);
        var global = await permissions.HasAsync(actor, "translations.export", AccessScope.Global, null, ct);
        var own = await permissions.HasAsync(actor, "translations.export", AccessScope.Own, actor, ct);
        var cases = await (from a in db.Access where a.SubjectId == actor && a.Scope == AccessScope.Case
                           join account in db.Users on a.SubjectId equals account.Id where account.Active
                           join p in db.Permissions on a.ProfileId equals p.ProfileId
                           where p.Permission == "translations.export" select a.ResourceId).ToArrayAsync(ct);
        if (!global && !own && cases.Length == 0) throw new AccessDeniedException();
        // A single statement fixes each exported row's case revision and fragment provenance.
        var rows = await (from c in db.Cases.AsNoTracking()
                          join f in db.Fragments.AsNoTracking() on c.FragmentId equals f.Id
                          join r in db.Resolutions.AsNoTracking() on c.ResolutionId equals r.Id into decisions
                          from r in decisions.DefaultIfEmpty()
                          where (global || own && f.ProducerId == actor || cases.Contains(c.Id)) &&
                              (includeDisputed || r != null && r.PrimaryVariantId != null && !c.NeedsExpert)
                          orderby c.CreatedAt, c.Id
                          select new { CaseId = c.Id, f.ExternalId, f.Text, f.LanguagePair, f.SourceJson, c.NeedsExpert,
                              VariantId = r != null && r.PrimaryVariantId != null ? r.PrimaryVariantId.Value : c.ShownVariantId,
                              Resolved = r != null && r.PrimaryVariantId != null }).Skip(offset).Take(limit).ToArrayAsync(ct);
        var result = new List<ExportRow>();
        foreach (var row in rows)
        {
            var variant = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == row.VariantId, ct);
            var opinions = await evidence.OpinionsAsync(row.CaseId, ct);
            var support = ReviewStatisticsCalculator.Calculate(opinions, [row.VariantId]).Variants[0].Supports;
            result.Add(new(row.ExternalId, row.Text, row.LanguagePair, variant.Text,
                JsonSerializer.Deserialize<SourceReference>(row.SourceJson)!, support,
                variant.Text == "" ? "untranslated" : row.Resolved && !row.NeedsExpert ? "confirmed" : "expert_review"));
        }
        await transaction.CommitAsync(ct);
        return result;
    }
}
