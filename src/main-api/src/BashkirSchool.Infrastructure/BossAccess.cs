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
        var exercises = await (from answer in db.Set<LevelAnswer>().AsNoTracking()
                               join question in db.Set<LevelQuestion>() on answer.QuestionId equals question.Id
                               join level in db.Set<LearningLevel>() on question.LevelId equals level.Id
                               where level.ParticipantId == participant && answer.Recognized
                               select answer.SubmittedAt).ToArrayAsync(ct);
        var completedLevels = await db.Set<LearningLevel>().LongCountAsync(x => x.ParticipantId == participant && x.CompletedAt != null, ct);
        var completedBosses = await db.Set<LevelBoss>().LongCountAsync(x => x.ParticipantId == participant && x.CompletedAt != null, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
        var dates = reviews.Concat(exercises).Select(date =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime)).ToHashSet();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var cursor = dates.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (dates.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        var solved = reviews.Length + exercises.Length;
        return new(solved, streak, completedLevels / 5 > completedBosses);
    }
}
