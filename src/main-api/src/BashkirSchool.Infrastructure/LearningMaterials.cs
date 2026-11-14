using System.Security.Cryptography;
using System.Text;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class LearningMaterials(CoreDbContext db, IPermissionChecker permissions)
{
    public async Task<VocabularyDeliveryResponse> DeliverAsync(Guid actor, DeliverVocabularyRequest request, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "content.deliver", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
        var pair = Guard.Text(request.LanguagePair, "languagePair", 7);
        var languages = pair.Split('-');
        if (languages.Length != 2 || languages.Any(x => x.Length is < 2 or > 3 || !x.All(char.IsAsciiLetterLower)) || languages[0] == languages[1])
            throw new DomainException("Use a directed language pair such as ba-ru.");
        if (request.Entries is null || request.Entries.Count is < 1 or > 500) throw new DomainException("Supply 1..500 vocabulary entries.");
        var entries = request.Entries.Select(x => x is null ? throw new DomainException("Vocabulary entry is required.")
            : new VocabularyItemRequest(Guard.Text(x.Word, "word", 200), Guard.Text(x.Translation, "translation", 200))).ToArray();
        if (entries.Any(x => x.Word.Any(char.IsWhiteSpace) || x.Translation.Any(char.IsWhiteSpace))) throw new DomainException("Vocabulary matching uses single words.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71830425)", ct);
        var words = entries.Select(x => x.Word).Distinct().ToArray();
        var existing = await db.Set<VocabularyEntry>().Where(x => x.LanguagePair == pair && words.Contains(x.Word)).ToListAsync(ct);
        var ids = new List<Guid>();
        foreach (var item in entries)
        {
            var entry = existing.FirstOrDefault(x => x.Word == item.Word && x.Translation == item.Translation);
            if (entry is null)
            {
                entry = new() { LanguagePair = pair, Word = item.Word, Translation = item.Translation };
                db.Add(entry);
                existing.Add(entry);
            }
            ids.Add(entry.Id);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ids);
    }
}

public sealed class AudioPreparation(CoreDbContext db, IPermissionChecker permissions, ISpeechSynthesizer speech,
    IAudioStorage storage, IConfiguration configuration, TimeProvider clock)
{
    // Same-process deduplication; PostgreSQL's transaction advisory lock covers other app replicas.
    private static readonly SemaphoreSlim Gate = new(1);
    public async Task<AudioAssetResponse> PrepareAsync(Guid actor, Guid caseId, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "lessons.receive", AccessScope.Own, actor, ct) &&
            !await permissions.HasAsync(actor, "content.deliver", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
        return await PrepareCoreAsync(caseId, ct);
    }

    public async Task<AudioAssetResponse> PrepareVerifiedAsync(Guid caseId, CancellationToken ct)
    {
        if (!await db.Cases.AsNoTracking().AnyAsync(c => c.Id == caseId && c.ExpertClosed && c.ResolutionId != null &&
            db.Resolutions.Any(r => r.Id == c.ResolutionId && r.Kind == ResolutionKind.Expert && r.PrimaryVariantId != null), ct))
            throw new MissingResourceException();
        return await PrepareCoreAsync(caseId, ct);
    }

    private async Task<AudioAssetResponse> PrepareCoreAsync(Guid caseId, CancellationToken ct)
    {
        var fragment = await (from c in db.Cases where c.Id == caseId join f in db.Fragments on c.FragmentId equals f.Id select f).SingleOrDefaultAsync(ct)
            ?? throw new MissingResourceException();
        await Gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71830426)", ct);
            var language = fragment.LanguagePair.Split('-')[0];
            var voice = configuration[$"Speech:Speakers:{language}"] ?? (language == "ba" ? "bak_aigul" : "ru_bak_aigul");
            var identity = string.Join("\n", "v5_cis_base", configuration["Speech:ModelRevision"] ?? "1", voice,
                configuration["Speech:SampleRate"] ?? "24000", language, fragment.Text);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var key = $"speech/v5_cis_base/{hash}.wav";
            var prior = await db.Set<SentenceAudio>().SingleOrDefaultAsync(x => x.FragmentId == fragment.Id && x.ObjectKey == key, ct);
            if (!await storage.ExistsAsync(key, ct))
            {
                var result = await speech.SynthesizeAsync(fragment.Text, language, ct);
                await storage.UploadAsync(key, result, ct);
            }
            if (prior is not null) return new(prior.Id, fragment.Id);
            var asset = new SentenceAudio { FragmentId = fragment.Id, ObjectKey = key, ContentType = "audio/wav", CreatedAt = clock.GetUtcNow() };
            db.Add(asset);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(asset.Id, fragment.Id);
        }
        finally { Gate.Release(); }
    }
}
