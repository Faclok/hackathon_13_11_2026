using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed record BossAccessResponse(int TasksSolved, int Streak, bool CanAccess);

public sealed class BossAccess(CoreDbContext db, TimeProvider clock, IConfiguration configuration)
{
    public async Task<BossAccessResponse> ReadAsync(Guid participant, CancellationToken ct)
    {
        var reviews = await db.Responses.AsNoTracking()
            .Where(x => x.ParticipantId == participant && x.Action != ReviewAction.Skip)
            .Select(x => x.SubmittedAt).ToArrayAsync(ct);
        var exercises = await (from attempt in db.Set<ExerciseAttempt>().AsNoTracking()
                               join exercise in db.Set<Exercise>() on attempt.ExerciseId equals exercise.Id
                               join lesson in db.Set<DailyLesson>() on exercise.LessonId equals lesson.Id
                               where lesson.ParticipantId == participant
                               select attempt.SubmittedAt).ToArrayAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
        var dates = reviews.Concat(exercises).Select(date =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime)).ToHashSet();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var cursor = dates.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (dates.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        var solved = reviews.Length + exercises.Length;
        return new(solved, streak, solved >= 250 && streak >= 14);
    }
}
