using System.Text.Json;
using System.Text.Json.Nodes;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class LearningWorkflow(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock,
    IConfiguration configuration, ExerciseRegistry registry, IAudioStorage audioStorage, AudioPreparation audioPreparation)
{
    public async Task<LessonResponse> TodayAsync(Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "lessons.receive", ct);
        await Lock(ct);
        var lesson = await FindLesson(actor, ct);
        if (lesson is null)
        {
            var size = configuration.GetValue("Learning:LessonSize", 10);
            if (size is < 1 or > 100) throw new InvalidOperationException("Learning:LessonSize must be 1..100.");
            var kinds = configuration.GetSection("Learning:Kinds").Get<string[]>() ?? ["translation-text", "missing-word", "sentence-order", "audio-dictation", "audio-translation", "word-match"];
            if (kinds.Length == 0) throw new InvalidOperationException("Learning:Kinds cannot be empty.");
            foreach (var kind in kinds) registry.Latest(kind);
            lesson = NewLesson(actor);
            var sentences = await Sentences(null, ct);
            var count = 0;
            foreach (var sentence in sentences)
            {
                var material = new ExerciseMaterial(sentence, await Words(sentence.LanguagePair, ct), sentence.LanguagePair);
                var orderedKinds = kinds.Skip(count % kinds.Length).Concat(kinds.Take(count % kinds.Length));
                foreach (var kind in orderedKinds)
                {
                    var handler = registry.Latest(kind);
                    if (!handler.RequiresSentence) continue;
                    if (handler.Create(material) is not { } draft) continue;
                    Store(lesson.Id, handler, material, draft);
                    count++;
                    break;
                }
                if (count >= size) break;
            }
            foreach (var handler in kinds.Select(kind => registry.Latest(kind)).Where(h => !h.RequiresSentence))
            {
                if (count >= size) break;
                var material = new ExerciseMaterial(null, await Words("ba-ru", ct), "ba-ru");
                if (handler.Create(material) is { } draft) { Store(lesson.Id, handler, material, draft); count++; }
            }
            await db.SaveChangesAsync(ct);
        }
        var result = await ViewLesson(lesson, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<ExerciseResponse?> IssueAsync(Guid actor, IssueExerciseRequest request, CancellationToken ct)
    {
        var handler = registry.Latest(request.Kind);
        if (!handler.RequiresSentence && request.CaseId.HasValue) throw new DomainException("This kind does not take caseId.");
        ValidateLanguagePair(request.LanguagePair);
        await Require(actor, "lessons.receive", ct);
        // Resume the original snapshot even when the source later loses its accepted translation.
        var existingLesson = await FindLesson(actor, ct);
        if (existingLesson is not null)
        {
            var existing = await db.Set<Exercise>().Where(x => x.LessonId == existingLesson.Id && x.Kind == request.Kind && x.LanguagePair == request.LanguagePair
                && (!request.CaseId.HasValue || x.CaseId == request.CaseId)).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (existing is not null) return await View(existing, ct);
        }
        if (handler.RequiresAudio)
        {
            var candidates = await Sentences(request.CaseId, ct);
            var sentence = candidates.FirstOrDefault(s => s.LanguagePair == request.LanguagePair &&
                (!handler.RequiresAcceptedTranslation || s.Accepted.Count > 0));
            if (sentence is null) return null;
            await audioPreparation.PrepareAsync(actor, sentence.CaseId, ct);
            request = request with { CaseId = sentence.CaseId };
        }
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "lessons.receive", ct);
        await Lock(ct);
        var lesson = await FindLesson(actor, ct) ?? NewLesson(actor);
        var prior = await db.Set<Exercise>().Where(x => x.LessonId == lesson.Id && x.Kind == request.Kind && x.LanguagePair == request.LanguagePair
            && (!request.CaseId.HasValue || x.CaseId == request.CaseId)).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (prior is not null) return await View(prior, ct);
        if (await db.Set<Exercise>().CountAsync(x => x.LessonId == lesson.Id, ct) >= 100) throw new ConflictException("Daily exercise limit reached.");
        var words = await Words(request.LanguagePair, ct);
        var materials = !handler.RequiresSentence
            ? new[] { new ExerciseMaterial(null, words, request.LanguagePair) }
            : (await Sentences(request.CaseId, ct)).Where(s => s.LanguagePair == request.LanguagePair)
                .Select(s => new ExerciseMaterial(s, words, s.LanguagePair)).ToArray();
        foreach (var material in materials)
        {
            if (handler.Create(material) is not { } draft) continue;
            var exercise = Store(lesson.Id, handler, material, draft);
            await db.SaveChangesAsync(ct);
            var result = await View(exercise, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        return null;
    }

    // Allocate through the same review workflow as standalone translation checks.
    // The lesson keeps its assignment so progress survives switching channels.
    public async Task<AssignmentResponse?> StartBossAsync(Guid actor, IReviewWorkflow reviews, CancellationToken ct)
    {
        var today = await TodayAsync(actor, ct);
        if (today.Exercises.Any(x => !x.Completed)) throw new ConflictException("Complete the daily exercises before the boss.");
        await Require(actor, "reviews.receive", ct);
        var lesson = await db.Set<DailyLesson>().SingleAsync(x => x.Id == today.Id, ct);
        if (today.BossCompleted) return null;
        var assignment = await reviews.ReceiveAsync(actor, "translation-confirm", ct);
        if (assignment is null) return null;
        lesson.BossAssignmentId = assignment.Id;
        await db.SaveChangesAsync(ct);
        return assignment;
    }

    public Task<AttemptResponse> SubmitAsync(Guid actor, Guid exerciseId, SubmitAttemptRequest request, CancellationToken ct) =>
        SubmitCore(actor, exerciseId, ExerciseJson.Element(new TextExerciseAnswer(request.Answer)), true, ct);

    public Task<AttemptResponse> AnswerAsync(Guid actor, Guid exerciseId, SubmitExerciseAnswerRequest request, CancellationToken ct) =>
        SubmitCore(actor, exerciseId, request.Answer, false, ct);

    private async Task<AttemptResponse> SubmitCore(Guid actor, Guid exerciseId, JsonElement answer, bool legacy, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "exercises.submit", ct);
        await Lock(ct);
        var exercise = await (from e in db.Set<Exercise>() where e.Id == exerciseId
                              join l in db.Set<DailyLesson>() on e.LessonId equals l.Id where l.ParticipantId == actor select e).SingleOrDefaultAsync(ct)
            ?? throw new MissingResourceException();
        if (legacy && exercise.Kind != "translation-text") throw new DomainException("Use the typed answer endpoint for this exercise kind.");
        if (answer.ValueKind != JsonValueKind.Object || answer.GetRawText().Length > 20000) throw new DomainException("Answer must be an object of at most 20000 characters.");
        var handler = registry.Get(exercise.Kind, exercise.SchemaVersion);
        using var key = JsonDocument.Parse(exercise.AnswerKeyJson == "{}" && exercise.Kind == "translation-text" ? exercise.AcceptedTextsJson : exercise.AnswerKeyJson);
        var recognized = handler.Evaluate(key.RootElement, answer);
        var canonical = Canonical(answer);
        var prior = await db.Set<ExerciseAttempt>().SingleOrDefaultAsync(x => x.ExerciseId == exerciseId, ct);
        if (prior is not null)
        {
            var legacyText = exercise.Kind == "translation-text" ? ExerciseJson.Read<TextExerciseAnswer>(answer).Text : null;
            if (prior.Answer != canonical && prior.Answer != legacyText) throw new ConflictException("Exercise was already submitted with a different answer.");
            return new(prior.Id, prior.Recognized);
        }
        var attempt = new ExerciseAttempt { ExerciseId = exerciseId, Answer = canonical, Recognized = recognized, SubmittedAt = clock.GetUtcNow() };
        db.Add(attempt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(attempt.Id, attempt.Recognized);
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };

    private Exercise Store(Guid lessonId, IExerciseHandler handler, ExerciseMaterial material, ExerciseDraft draft)
    {
        var exercise = new Exercise { LessonId = lessonId, Kind = handler.Kind, SchemaVersion = handler.SchemaVersion,
            LanguagePair = material.LanguagePair, CaseId = material.Sentence?.CaseId, ResolutionId = material.Sentence?.ResolutionId,
            Original = draft.Original, PromptJson = draft.Prompt.GetRawText(), AnswerKeyJson = draft.AnswerKey.GetRawText(),
            AcceptedTextsJson = handler.Kind == "translation-text" ? draft.AnswerKey.GetRawText() : "[]" };
        db.Add(exercise);
        return exercise;
    }
    private async Task<ExerciseSentence[]> Sentences(Guid? caseId, CancellationToken ct)
    {
        var rows = await (from c in db.Cases join f in db.Fragments on c.FragmentId equals f.Id
                          where !caseId.HasValue || c.Id == caseId
                          orderby c.CreatedAt, c.Id select new { c.Id, c.ResolutionId, f.Text, f.LanguagePair, FragmentId = f.Id }).Take(500).ToArrayAsync(ct);
        var resolutionIds = rows.Where(x => x.ResolutionId.HasValue).Select(x => x.ResolutionId!.Value).ToArray();
        var resolutions = await db.Resolutions.Where(x => resolutionIds.Contains(x.Id) && x.Kind != ResolutionKind.Pending).ToDictionaryAsync(x => x.Id, ct);
        var acceptedIds = resolutions.Values.SelectMany(x => x.Accepted).Distinct().ToArray();
        var variants = await db.Variants.Where(x => acceptedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Text, ct);
        var fragments = rows.Select(x => x.FragmentId).ToArray();
        var audioRows = await db.Set<SentenceAudio>().Where(x => fragments.Contains(x.FragmentId)).ToArrayAsync(ct);
        var audio = audioRows.GroupBy(x => x.FragmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First().Id);
        return rows.Select(x =>
        {
            var resolution = x.ResolutionId.HasValue ? resolutions.GetValueOrDefault(x.ResolutionId.Value) : null;
            return new ExerciseSentence(x.Id, resolution?.Id, x.Text, x.LanguagePair,
                resolution?.Accepted.Select(id => variants[id]).ToArray() ?? [], audio.TryGetValue(x.FragmentId, out var id) ? id : null);
        }).ToArray();
    }
    private async Task<ExerciseWord[]> Words(string pair, CancellationToken ct) => await db.Set<VocabularyEntry>()
        .Where(x => x.LanguagePair == pair).OrderBy(x => x.Id).Take(500)
        .Select(x => new ExerciseWord(x.Id, x.Word, x.Translation)).ToArrayAsync(ct);

    private DateOnly Today()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
    private Task<DailyLesson?> FindLesson(Guid actor, CancellationToken ct) => db.Set<DailyLesson>().SingleOrDefaultAsync(x => x.ParticipantId == actor && x.Date == Today(), ct);
    private DailyLesson NewLesson(Guid actor) { var lesson = new DailyLesson { ParticipantId = actor, Date = Today() }; db.Add(lesson); return lesson; }
    private async Task<LessonResponse> ViewLesson(DailyLesson lesson, CancellationToken ct)
    {
        var exercises = await db.Set<Exercise>().Where(x => x.LessonId == lesson.Id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var result = new List<ExerciseResponse>();
        foreach (var exercise in exercises) result.Add(await View(exercise, ct));
        var bossCompleted = lesson.BossAssignmentId is Guid bossId && await db.Responses
            .AnyAsync(x => x.AssignmentId == bossId && x.Action != ReviewAction.Skip, ct);
        return new(lesson.Id, lesson.Date, result) { BossCompleted = bossCompleted };
    }
    private async Task<ExerciseResponse> View(Exercise exercise, CancellationToken ct)
    {
        var prompt = JsonNode.Parse(exercise.PromptJson)!.AsObject();
        if (exercise.PromptJson == "{}" && exercise.Kind == "translation-text") prompt["text"] = exercise.Original;
        if (prompt["audioId"] is { } idNode)
        {
            var audioId = idNode.GetValue<Guid>();
            var asset = await db.Set<SentenceAudio>().SingleAsync(x => x.Id == audioId, ct);
            prompt["audioUrl"] = await audioStorage.ListenUrlAsync(asset.ObjectKey, ct);
        }
        return new(exercise.Id, exercise.Original, await db.Set<ExerciseAttempt>().AnyAsync(x => x.ExerciseId == exercise.Id, ct))
        { Kind = exercise.Kind, SchemaVersion = exercise.SchemaVersion, Prompt = JsonSerializer.SerializeToElement(prompt) };
    }
    internal static void ValidateLanguagePair(string pair)
    {
        var languages = pair?.Split('-') ?? [];
        if (languages.Length != 2 || languages.Any(x => x.Length is < 2 or > 3 || !x.All(char.IsAsciiLetterLower)) || languages[0] == languages[1])
            throw new DomainException("Use a directed language pair such as ba-ru.");
    }
    private async Task Lock(CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71830424)", ct);
    }
    private async Task Require(Guid actor, string permission, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, permission, AccessScope.Own, actor, ct)) throw new AccessDeniedException();
    }
}
