using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class LearningWorkflow(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock, IConfiguration configuration)
{
    public async Task<LessonResponse> TodayAsync(Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "lessons.receive", ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var lesson = await db.Set<DailyLesson>().SingleOrDefaultAsync(x => x.ParticipantId == actor && x.Date == date, ct);
        if (lesson is null)
        {
            var size = configuration.GetValue("Learning:LessonSize", 10);
            if (size is < 1 or > 100) throw new InvalidOperationException("Learning:LessonSize must be 1..100.");
            lesson = new() { ParticipantId = actor, Date = date };
            db.Add(lesson);
            var materials = await (from c in db.Cases join r in db.Resolutions on c.ResolutionId equals r.Id
                                   join f in db.Fragments on c.FragmentId equals f.Id
                                   where r.PrimaryVariantId != null && r.Kind != ResolutionKind.Pending
                                   orderby c.CreatedAt, c.Id select new { c.Id, Resolution = r, f.Text }).Take(size).ToArrayAsync(ct);
            foreach (var m in materials)
            {
                var accepted = m.Resolution.Accepted;
                var texts = await db.Variants.Where(x => x.CaseId == m.Id && accepted.Contains(x.Id)).Select(x => x.Text).ToArrayAsync(ct);
                db.Add(new Exercise { LessonId = lesson.Id, CaseId = m.Id, ResolutionId = m.Resolution.Id, Original = m.Text, AcceptedTextsJson = JsonSerializer.Serialize(texts) });
            }
            await db.SaveChangesAsync(ct);
        }
        var exercises = await (from e in db.Set<Exercise>() where e.LessonId == lesson.Id
                               select new ExerciseResponse(e.Id, e.Original, db.Set<ExerciseAttempt>().Any(a => a.ExerciseId == e.Id))).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(lesson.Id, lesson.Date, exercises);
    }

    public async Task<AttemptResponse> SubmitAsync(Guid actor, Guid exerciseId, SubmitAttemptRequest request, CancellationToken ct)
    {
        Guard.Text(request.Answer, "answer");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await Require(actor, "exercises.submit", ct);
        var exercise = await (from e in db.Set<Exercise>() where e.Id == exerciseId
                              join l in db.Set<DailyLesson>() on e.LessonId equals l.Id where l.ParticipantId == actor select e).SingleOrDefaultAsync(ct)
            ?? throw new MissingResourceException();
        var prior = await db.Set<ExerciseAttempt>().SingleOrDefaultAsync(x => x.ExerciseId == exerciseId, ct);
        if (prior is not null)
        {
            if (prior.Answer != request.Answer) throw new ConflictException("Exercise was already submitted with a different answer.");
            return new(prior.Id, prior.Recognized);
        }
        var accepted = JsonSerializer.Deserialize<string[]>(exercise.AcceptedTextsJson)!;
        var attempt = new ExerciseAttempt { ExerciseId = exerciseId, Answer = request.Answer,
            Recognized = accepted.Contains(request.Answer, StringComparer.Ordinal), SubmittedAt = clock.GetUtcNow() };
        db.Add(attempt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(attempt.Id, attempt.Recognized);
    }
    private async Task Require(Guid actor, string permission, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, permission, AccessScope.Own, actor, ct)) throw new AccessDeniedException();
    }
}
