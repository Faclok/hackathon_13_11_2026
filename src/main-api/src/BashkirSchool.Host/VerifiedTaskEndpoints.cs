using System.Security.Cryptography;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Host;

public static class VerifiedTaskEndpoints
{
    public static void MapVerifiedTaskEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/public/verified-tasks").AllowAnonymous().WithTags("Public expert-verified tasks");
        api.MapGet("/", async (int? offset, int? limit, CoreDbContext db, IAudioStorage storage,
            IConfiguration configuration, CancellationToken ct) =>
        {
            var skip = offset ?? 0;
            var take = limit ?? 20;
            if (skip < 0 || take is < 1 or > 100) return Results.BadRequest(new { title = "Use offset >= 0 and limit 1..100." });
            var cases = await Verified(db).OrderBy(c => c.Id).Skip(skip).Take(take).ToArrayAsync(ct);
            var tasks = new List<VerifiedTaskResponse>(cases.Length);
            foreach (var item in cases) tasks.Add(await Read(item, db, storage, configuration, ct));
            return Results.Ok(tasks);
        }).WithName("ListPublicVerifiedTasks").WithSummary("List complete expert-verified translation tasks, including available TTS audio.")
            .Produces<IReadOnlyList<VerifiedTaskResponse>>().ProducesProblem(400);

        api.MapGet("/random", async (CoreDbContext db, IAudioStorage storage, IConfiguration configuration, CancellationToken ct) =>
        {
            var query = Verified(db);
            var count = await query.CountAsync(ct);
            if (count == 0) return Results.NotFound();
            var item = await query.OrderBy(c => c.Id).Skip(RandomNumberGenerator.GetInt32(count)).FirstAsync(ct);
            return Results.Ok(await Read(item, db, storage, configuration, ct));
        }).WithName("GetRandomPublicVerifiedTask").WithSummary("Get any expert-verified translation task in full.")
            .Produces<VerifiedTaskResponse>().ProducesProblem(404);

        api.MapGet("/{caseId:guid}", async (Guid caseId, CoreDbContext db, IAudioStorage storage,
            IConfiguration configuration, CancellationToken ct) =>
        {
            var item = await Verified(db).SingleOrDefaultAsync(c => c.Id == caseId, ct);
            return item is null ? Results.NotFound() : Results.Ok(await Read(item, db, storage, configuration, ct));
        }).WithName("GetPublicVerifiedTask").WithSummary("Get a complete expert-verified task by case ID.")
            .Produces<VerifiedTaskResponse>().ProducesProblem(404);

        api.MapPost("/{caseId:guid}/tts", async (Guid caseId, AudioPreparation audio, CoreDbContext db,
            IAudioStorage storage, IConfiguration configuration, CancellationToken ct) =>
        {
            await audio.PrepareVerifiedAsync(caseId, ct);
            var item = await Verified(db).SingleAsync(c => c.Id == caseId, ct);
            return Results.Ok((await Read(item, db, storage, configuration, ct)).Tts);
        }).RequireRateLimiting("public-tts").WithName("PreparePublicVerifiedTaskTts")
            .WithSummary("Generate or reuse Silero speech for an expert-verified task; returns a temporary listening URL.")
            .Produces<VerifiedTaskTts>().ProducesProblem(404).ProducesProblem(429);
    }

    private static IQueryable<TranslationCase> Verified(CoreDbContext db) => db.Cases.AsNoTracking()
        .Where(c => c.ExpertClosed && c.ResolutionId != null && db.Resolutions.Any(r =>
            r.Id == c.ResolutionId && r.Kind == ResolutionKind.Expert && r.PrimaryVariantId != null));

    private static async Task<VerifiedTaskResponse> Read(TranslationCase item, CoreDbContext db,
        IAudioStorage storage, IConfiguration configuration, CancellationToken ct)
    {
        var fragment = await db.Fragments.AsNoTracking().SingleAsync(f => f.Id == item.FragmentId, ct);
        var resolution = await db.Resolutions.AsNoTracking().SingleAsync(r => r.Id == item.ResolutionId, ct);
        var variants = await db.Variants.AsNoTracking().Where(v => v.CaseId == item.Id)
            .OrderBy(v => v.Id).ToArrayAsync(ct);
        var accepted = resolution.Accepted;
        var rejected = resolution.Rejected;
        var primary = variants.Single(v => v.Id == resolution.PrimaryVariantId);
        var audio = await db.Set<SentenceAudio>().AsNoTracking().Where(a => a.FragmentId == fragment.Id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var language = fragment.LanguagePair.Split('-')[0];
        var tts = new VerifiedTaskTts(language, configuration[$"Speech:Speakers:{language}"] ??
            (language == "ba" ? "bak_aigul" : "ru_bak_aigul"),
            configuration.GetValue("Speech:SampleRate", 24000), audio?.Id,
            audio is null ? null : await storage.ListenUrlAsync(audio.ObjectKey, ct), audio?.ContentType);
        return new VerifiedTaskResponse(item.Id, fragment.Id, fragment.ExternalId, fragment.Text,
            fragment.LanguagePair, JsonSerializer.Deserialize<JsonElement>(fragment.SourceJson),
            item.CreatedAt, item.Revision,
            new VerifiedTaskDecision(resolution.Id, resolution.CreatedAt, primary.Id, primary.Text,
                accepted, rejected, resolution.Comment),
            variants.Select(v => new VerifiedTaskVariant(v.Id, v.Text, v.MachineGenerated,
                accepted.Contains(v.Id), rejected.Contains(v.Id))).ToArray(), tts);
    }
}

public sealed record VerifiedTaskResponse(Guid CaseId, Guid FragmentId, string ExternalId, string SourceText,
    string LanguagePair, JsonElement Source, DateTimeOffset CreatedAt, long Revision,
    VerifiedTaskDecision Decision, IReadOnlyList<VerifiedTaskVariant> Variants, VerifiedTaskTts Tts);
public sealed record VerifiedTaskDecision(Guid ResolutionId, DateTimeOffset DecidedAt, Guid PrimaryVariantId,
    string PrimaryTranslation, IReadOnlyList<Guid> AcceptedVariantIds, IReadOnlyList<Guid> RejectedVariantIds, string? Comment);
public sealed record VerifiedTaskVariant(Guid Id, string Text, bool MachineGenerated, bool Accepted, bool Rejected);
public sealed record VerifiedTaskTts(string Language, string Speaker, int SampleRate, Guid? AudioId,
    string? AudioUrl, string? ContentType);
