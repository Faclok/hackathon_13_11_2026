using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class LevelWorkflow(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock,
    IConfiguration configuration, ExerciseRegistry registry, IAudioStorage audioStorage, IReviewWorkflow reviews)
{
    public async Task<LevelResponse?> CurrentAsync(Guid actor, CancellationToken ct)
    {
        await Require(actor, "lessons.receive", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(actor, ct);
        var level = await db.Set<LearningLevel>().SingleOrDefaultAsync(x => x.ParticipantId == actor && x.CompletedAt == null, ct);
        if (level is null)
        {
            var number = (await db.Set<LearningLevel>().Where(x => x.ParticipantId == actor).MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
            var questions = await CreateQuestions(number, ct);
            if (questions.Count == 0) return null;
            level = new LearningLevel { ParticipantId = actor, Number = number, StartedAt = clock.GetUtcNow() };
            db.Add(level);
            foreach (var question in questions) { question.LevelId = level.Id; db.Add(question); }
            await db.SaveChangesAsync(ct);
        }
        var result = await View(level, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<LevelAnswerResponse> AnswerAsync(Guid actor, Guid levelId, Guid questionId, AnswerLevelRequest request, CancellationToken ct)
    {
        await Require(actor, "exercises.submit", ct);
        if (request.AttemptToken == Guid.Empty || request.Answer.ValueKind != JsonValueKind.Object || request.Answer.GetRawText().Length > 20000)
            throw new DomainException("Supply the issued attemptToken and an answer object of at most 20000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(actor, ct);
        var level = await db.Set<LearningLevel>().SingleOrDefaultAsync(x => x.Id == levelId && x.ParticipantId == actor, ct)
            ?? throw new MissingResourceException();
        var question = await db.Set<LevelQuestion>().SingleOrDefaultAsync(x => x.Id == questionId && x.LevelId == level.Id, ct)
            ?? throw new MissingResourceException();
        var canonical = Canonical(request.Answer);
        var prior = await db.Set<LevelAnswer>().SingleOrDefaultAsync(x => x.Id == request.AttemptToken, ct);
        if (prior is not null)
        {
            if (prior.QuestionId != question.Id || prior.AnswerJson != canonical)
                throw new ConflictException("Attempt token was already used for a different answer.");
            return new(prior.Id, prior.Recognized, prior.LevelCompleted);
        }
        var questions = await Questions(level.Id, ct);
        var current = Next(questions);
        if (level.CompletedAt.HasValue || current?.Id != question.Id || request.AttemptToken != question.AttemptToken)
            throw new ConflictException("Only the current question and its current attempt token can be answered.");
        using var key = JsonDocument.Parse(question.AnswerKeyJson);
        var recognized = registry.Get(question.Kind, question.SchemaVersion).Evaluate(key.RootElement, request.Answer);
        if (recognized) question.CompletedAt = clock.GetUtcNow();
        else question.QueueOrder = questions.Max(x => x.QueueOrder) + 1;
        question.AttemptToken = Guid.NewGuid();
        if (questions.All(x => x.CompletedAt.HasValue)) level.CompletedAt = clock.GetUtcNow();
        var answer = new LevelAnswer { Id = request.AttemptToken, QuestionId = question.Id, AnswerJson = canonical,
            Recognized = recognized, SubmittedAt = clock.GetUtcNow(), LevelCompleted = level.CompletedAt.HasValue };
        db.Add(answer);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(answer.Id, recognized, answer.LevelCompleted);
    }

    public async Task<LevelProgressResponse> ProgressAsync(Guid actor, CancellationToken ct)
    {
        await Require(actor, "lessons.receive", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(actor, ct);
        var completed = await db.Set<LearningLevel>().LongCountAsync(x => x.ParticipantId == actor && x.CompletedAt != null, ct);
        var bosses = await db.Set<LevelBoss>().LongCountAsync(x => x.ParticipantId == actor && x.CompletedAt != null, ct);
        var answers = from a in db.Set<LevelAnswer>() join q in db.Set<LevelQuestion>() on a.QuestionId equals q.Id
                      join l in db.Set<LearningLevel>() on q.LevelId equals l.Id where l.ParticipantId == actor select a;
        var result = new LevelProgressResponse(completed, bosses, completed / 5 - bosses, await answers.LongCountAsync(ct),
            await answers.LongCountAsync(x => !x.Recognized, ct));
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<LevelStatisticsResponse>> StatisticsAsync(Guid actor, int offset, int limit, CancellationToken ct)
    {
        await Require(actor, "lessons.receive", ct);
        if (offset < 0 || limit is < 1 or > 100) throw new DomainException("Invalid pagination.");
        var levels = await db.Set<LearningLevel>().Where(x => x.ParticipantId == actor).OrderByDescending(x => x.Number)
            .Skip(offset).Take(limit).ToArrayAsync(ct);
        var result = new List<LevelStatisticsResponse>();
        foreach (var level in levels)
        {
            var questions = await Questions(level.Id, ct);
            var ids = questions.Select(x => x.Id).ToArray();
            var answers = await db.Set<LevelAnswer>().Where(x => ids.Contains(x.QuestionId)).ToArrayAsync(ct);
            result.Add(new(level.Number, questions.Length, questions.Count(x => x.CompletedAt.HasValue), answers.Length,
                answers.Count(x => !x.Recognized), level.StartedAt, level.CompletedAt));
        }
        return result;
    }

    public async Task<LevelBossResponse?> BossAsync(Guid actor, CancellationToken ct)
    {
        await Require(actor, "lessons.receive", ct);
        await Require(actor, "reviews.receive", ct);
        LevelBoss boss;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await Lock(actor, ct);
            var completed = await db.Set<LearningLevel>().LongCountAsync(x => x.ParticipantId == actor && x.CompletedAt != null, ct);
            var existing = await db.Set<LevelBoss>().SingleOrDefaultAsync(x => x.ParticipantId == actor && x.CompletedAt == null, ct);
            if (existing is null)
            {
                var last = await db.Set<LevelBoss>().Where(x => x.ParticipantId == actor).MaxAsync(x => (long?)x.Checkpoint, ct) ?? 0;
                if (completed < last + 5) return null;
                existing = new LevelBoss { ParticipantId = actor, Checkpoint = last + 5, StartedAt = clock.GetUtcNow() };
                db.Add(existing);
                await db.SaveChangesAsync(ct);
            }
            boss = existing;
            await ReconcileBoss(boss, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var count = await db.Set<LevelBossQuestion>().CountAsync(x => x.BossId == boss.Id && x.CompletedAt != null, ct);
        if (boss.CompletedAt.HasValue) return new(boss.Id, boss.Checkpoint, 3, count, null);

        // Reviews own their transaction and allocation quotas. Persisting a slot is a separate,
        // repeatable step: an interrupted request resumes the same active review assignment.
        var completedCases = await (from q in db.Set<LevelBossQuestion>()
                                    join a in db.Assignments on q.AssignmentId equals a.Id
                                    where q.BossId == boss.Id && q.CompletedAt != null select a.CaseId).ToArrayAsync(ct);
        var assignment = await reviews.ReceiveAsync(actor, "translation-auto", completedCases, ct);
        if (assignment is null) return new(boss.Id, boss.Checkpoint, 3, count, null);
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await Lock(actor, ct);
            await db.Entry(boss).ReloadAsync(ct);
            await ReconcileBoss(boss, ct);
            count = await db.Set<LevelBossQuestion>().CountAsync(x => x.BossId == boss.Id && x.CompletedAt != null, ct);
            if (boss.CompletedAt.HasValue) throw new ConflictException("Boss has already been completed.");
            var slot = await db.Set<LevelBossQuestion>().SingleOrDefaultAsync(x => x.BossId == boss.Id && x.CompletedAt == null, ct);
            if (slot is null) db.Add(new LevelBossQuestion { BossId = boss.Id, Position = count, AssignmentId = assignment.Id });
            else slot.AssignmentId = assignment.Id;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return new(boss.Id, boss.Checkpoint, 3, count, assignment);
    }

    public async Task<LevelBossAnswerResponse> AnswerBossAsync(Guid actor, Guid bossId, Guid assignmentId,
        SubmitReviewRequest request, CancellationToken ct)
    {
        await Require(actor, "reviews.submit", ct);
        var boss = await db.Set<LevelBoss>().SingleOrDefaultAsync(x => x.Id == bossId && x.ParticipantId == actor, ct)
            ?? throw new MissingResourceException();
        if (!await db.Set<LevelBossQuestion>().AnyAsync(x => x.BossId == boss.Id && x.AssignmentId == assignmentId, ct))
            throw new MissingResourceException();
        var receipt = await reviews.SubmitAsync(actor, assignmentId, request, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(actor, ct);
        await db.Entry(boss).ReloadAsync(ct);
        await ReconcileBoss(boss, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(receipt.ResponseId, receipt.Archived, boss.CompletedAt.HasValue);
    }

    private async Task ReconcileBoss(LevelBoss boss, CancellationToken ct)
    {
        var slots = await db.Set<LevelBossQuestion>().Where(x => x.BossId == boss.Id).ToArrayAsync(ct);
        foreach (var slot in slots.Where(x => !x.CompletedAt.HasValue))
        {
            var response = await db.Responses.SingleOrDefaultAsync(x => x.AssignmentId == slot.AssignmentId && x.Action != ReviewAction.Skip, ct);
            if (response is null) continue;
            slot.ResponseId = response.Id;
            slot.CompletedAt = response.SubmittedAt;
        }
        if (slots.Count(x => x.CompletedAt.HasValue) == 3) boss.CompletedAt ??= clock.GetUtcNow();
        // Flush reconciled slots before subsequent aggregate queries in this transaction.
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<LevelQuestion>> CreateQuestions(long number, CancellationToken ct)
    {
        var kinds = configuration.GetSection("Learning:Kinds").Get<string[]>() ??
            ["translation-text", "missing-word", "sentence-order", "audio-dictation", "audio-translation", "word-match"];
        if (kinds.Length == 0) throw new InvalidOperationException("Learning:Kinds cannot be empty.");
        var handlers = kinds.Select(registry.Latest).ToArray();
        var rows = await (from c in db.Cases join f in db.Fragments on c.FragmentId equals f.Id
                          join r in db.Resolutions on c.ResolutionId equals r.Id
                          where r.Kind == ResolutionKind.Community || r.Kind == ResolutionKind.Expert
                          orderby c.CreatedAt, c.Id
                          select new { CaseId = c.Id, ResolutionId = r.Id, r.AcceptedJson, f.Id, f.Text, f.LanguagePair })
            .Take(500).ToArrayAsync(ct);
        if (rows.Length == 0) return [];
        var acceptedIds = rows.SelectMany(x => JsonSerializer.Deserialize<Guid[]>(x.AcceptedJson)!).Distinct().ToArray();
        var variants = await db.Variants.Where(x => acceptedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Text, ct);
        var fragmentIds = rows.Select(x => x.Id).ToArray();
        var audio = (await db.Set<SentenceAudio>().Where(x => fragmentIds.Contains(x.FragmentId)).ToArrayAsync(ct))
            .GroupBy(x => x.FragmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First().Id);
        var words = await db.Set<VocabularyEntry>().OrderBy(x => x.Id).Take(500).ToArrayAsync(ct);
        var drafts = new List<LevelQuestion>();
        var size = Random.Shared.Next(3, 7);
        // Cycle all available kinds/materials before reusing one; a small corpus can still
        // supply a full level, and each issued answer key remains an immutable snapshot.
        var candidates = new List<(IExerciseHandler Handler, ExerciseMaterial Material)>();
        foreach (var row in rows)
        {
            var sentence = new ExerciseSentence(row.CaseId, row.ResolutionId, row.Text, row.LanguagePair,
                JsonSerializer.Deserialize<Guid[]>(row.AcceptedJson)!.Select(id => variants[id]).ToArray(),
                audio.TryGetValue(row.Id, out var audioId) ? audioId : null);
            var vocabulary = words.Where(x => x.LanguagePair == row.LanguagePair).Select(x => new ExerciseWord(x.Id, x.Word, x.Translation)).ToArray();
            foreach (var handler in handlers)
                candidates.Add((handler, new(handler.RequiresSentence ? sentence : null, vocabulary, row.LanguagePair)));
        }
        var offset = (int)((number - 1) % candidates.Count);
        var available = candidates.Skip(offset).Concat(candidates.Take(offset)).Where(x => x.Handler.Create(x.Material) is not null).ToArray();
        if (available.Length == 0) return [];
        for (var i = 0; i < size; i++)
        {
            var (handler, material) = available[i % available.Length];
            var draft = handler.Create(material)!;
            drafts.Add(new LevelQuestion { Position = i, QueueOrder = i, Kind = handler.Kind, SchemaVersion = handler.SchemaVersion,
                CaseId = material.Sentence?.CaseId, ResolutionId = material.Sentence?.ResolutionId,
                PromptJson = draft.Prompt.GetRawText(), AnswerKeyJson = draft.AnswerKey.GetRawText() });
        }
        return drafts;
    }

    private async Task<LevelResponse> View(LearningLevel level, CancellationToken ct)
    {
        var questions = await Questions(level.Id, ct);
        var question = Next(questions) ?? throw new ConflictException("Level is already completed.");
        var prompt = JsonNode.Parse(question.PromptJson)!.AsObject();
        if (prompt["audioId"] is { } audioNode)
        {
            var audioId = audioNode.GetValue<Guid>();
            var asset = await db.Set<SentenceAudio>().SingleAsync(x => x.Id == audioId, ct);
            prompt["audioUrl"] = await audioStorage.ListenUrlAsync(asset.ObjectKey, ct);
        }
        return new(level.Id, level.Number, questions.Length, questions.Count(x => x.CompletedAt.HasValue),
            new(question.Id, question.AttemptToken, question.Kind, question.SchemaVersion, JsonSerializer.SerializeToElement(prompt)));
    }

    private Task<LevelQuestion[]> Questions(Guid levelId, CancellationToken ct) =>
        db.Set<LevelQuestion>().Where(x => x.LevelId == levelId).OrderBy(x => x.Position).ToArrayAsync(ct);
    private static LevelQuestion? Next(IEnumerable<LevelQuestion> questions) =>
        questions.Where(x => !x.CompletedAt.HasValue).OrderBy(x => x.QueueOrder).FirstOrDefault();
    private async Task Require(Guid actor, string permission, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, permission, AccessScope.Own, actor, ct)) throw new AccessDeniedException();
    }
    private async Task Lock(Guid actor, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.ToString()}, 71830425))", ct);
    }
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
}
