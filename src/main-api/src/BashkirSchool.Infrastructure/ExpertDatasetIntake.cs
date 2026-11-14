using System.Data;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ExpertDatasetIntake(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock)
{
    public async Task<ExpertDatasetReceipt> ImportAsync(Guid actor, ExpertDatasetRow row, CancellationToken ct)
    {
        if (!await CanImport(actor, ct)) throw new AccessDeniedException();
        if (row.Source is null) throw new DomainException("Source is required.");
        Guard.Text(row.Source.DocumentId, "source.documentId", 200);
        if (row.Source.StartSeconds < 0 || row.Source.EndSeconds < 0 ||
            row.Source.StartSeconds.HasValue != row.Source.EndSeconds.HasValue ||
            row.Source.StartSeconds.HasValue && row.Source.EndSeconds <= row.Source.StartSeconds)
            throw new DomainException("Invalid source interval.");
        var externalId = Guard.Text(row.FragmentId, "fragmentId", 200);
        var text = Guard.Text(row.TextBa, "textBa");
        if (row.LangPair != "ba-ru") throw new DomainException("Dataset import supports ba-ru only.");
        if (row.ExpertVerified != !string.IsNullOrWhiteSpace(row.Translation))
            throw new DomainException("Verified rows need a translation; untranslated rows must have none.");
        if (row.Translation is not null) Guard.Text(row.Translation, "translation");
        var sourceJson = JsonSerializer.Serialize(row.Source);
        if (sourceJson.Length > 10000) throw new DomainException("Source metadata is too long.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var existing = await db.Fragments.SingleOrDefaultAsync(x => x.ProducerId == actor && x.ExternalId == externalId && x.LanguagePair == row.LangPair, ct);
        if (existing is not null)
        {
            var existingCase = await db.Cases.SingleOrDefaultAsync(x => x.FragmentId == existing.Id, ct);
            var existingText = existingCase is null ? null : (await db.Variants.SingleAsync(x => x.Id == existingCase.ShownVariantId, ct)).Text;
            if (existing.Text != text || existing.SourceJson != sourceJson || existingText != row.Translation ||
                (existingCase?.ExpertClosed ?? false) != row.ExpertVerified)
                throw new ConflictException("Fragment ID already exists with different data or status.");
            await transaction.CommitAsync(ct);
            return new(existing.Id, existingCase?.Id, existingCase?.ShownVariantId, row.ExpertVerified ? "expert_verified" : "untranslated");
        }

        var fragment = Fragment.Create(actor, externalId, text, row.LangPair, sourceJson);
        db.Fragments.Add(fragment);
        if (!row.ExpertVerified)
        {
            db.Audit.Add(new() { ActorId = actor, TargetId = fragment.Id, Action = "dataset.imported.untranslated",
                Reason = row.Source.DocumentId, At = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(fragment.Id, null, null, "untranslated");
        }

        var receipt = await CreateVerified(fragment, row.Translation!, null, true, ct);
        db.Audit.Add(new() { ActorId = actor, TargetId = receipt.CaseId!.Value, Action = "dataset.imported.expert_verified",
            Reason = row.Source.DocumentId, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return receipt;
    }

    public async Task<IReadOnlyList<UntranslatedFragment>> UntranslatedAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        if (!await CanImport(actor, ct)) throw new AccessDeniedException();
        if (offset < 0 || limit is < 1 or > 1000) throw new DomainException("Invalid pagination.");
        var rows = await db.Fragments.AsNoTracking().Where(f => !db.Cases.Any(c => c.FragmentId == f.Id))
            .OrderBy(f => f.ExternalId).ThenBy(f => f.Id).Skip(offset).Take(limit).ToArrayAsync(ct);
        return rows.Select(f => new UntranslatedFragment(f.Id, f.ExternalId, f.Text, f.LanguagePair,
            JsonSerializer.Deserialize<SourceReference>(f.SourceJson)!)).ToArray();
    }

    public async Task<ExpertDatasetReceipt> TranslateAsync(Guid actor, Guid fragmentId, VerifiedFragmentTranslation request, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "expert.translation.resolve", AccessScope.Global, null, ct)) throw new AccessDeniedException();
        var translation = Guard.Text(request.Translation, "translation");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var fragment = await db.Fragments.SingleOrDefaultAsync(x => x.Id == fragmentId, ct) ?? throw new MissingResourceException();
        if (await db.Cases.AnyAsync(x => x.FragmentId == fragmentId, ct)) throw new ConflictException("Fragment already has a translation case.");
        var receipt = await CreateVerified(fragment, translation, actor, false, ct);
        db.Audit.Add(new() { ActorId = actor, TargetId = receipt.CaseId!.Value, Action = "expert.fragment.translated",
            At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return receipt;
    }

    private async Task<ExpertDatasetReceipt> CreateVerified(Fragment fragment, string translation, Guid? expertId, bool imported, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var variantId = Guid.NewGuid();
        var translationCase = TranslationCase.Open(fragment.Id, variantId, now);
        var variant = TranslationVariant.Create(translationCase.Id, translation, false, variantId, expertId, now);
        var policy = await db.Policies.OrderByDescending(x => x.Version).FirstAsync(ct);
        var evidence = JsonSerializer.Serialize(new { provenance = imported ? "dataset_expert_verified" : "expert_submission",
            source = JsonSerializer.Deserialize<SourceReference>(fragment.SourceJson) });
        var resolution = imported
            ? ResolutionRevision.ImportVerified(translationCase.Id, variantId, policy.Id, evidence, now)
            : ResolutionRevision.Create(translationCase.Id, 1, ResolutionKind.Expert, variantId, [variantId], [], policy.Id,
                expertId, evidence, "Перевод добавлен экспертом", now);
        translationCase.Apply(resolution);
        db.AddRange(translationCase, variant, resolution);
        return new(fragment.Id, translationCase.Id, variant.Id, "expert_verified");
    }

    private async Task<bool> CanImport(Guid actor, CancellationToken ct) =>
        await permissions.HasAsync(actor, "expert.translation.resolve", AccessScope.Global, null, ct) ||
        await permissions.HasAsync(actor, "access.assign", AccessScope.Global, null, ct);
}
