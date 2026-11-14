using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Assigns daily tasks and turns user answers into a consensus translation.</summary>
public sealed class TaskService(AppDbContext db)
{
    /// <summary>Gets or creates today's task and includes any prior answer.</summary>
    public async Task<Dictionary<string, object?>?> Today(Guid userId)
    {
        // The daily task changes at midnight in the product's Moscow time zone.
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var existing = await db.DailyTasks.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.LangPair == "ba-ru" && t.TaskDate == date);
        if (existing is null)
        {
            var count = await db.Fragments.CountAsync();
            if (count == 0) return null;
            // Every user sees the same fragment on a given calendar day.
            var offset = date.DayNumber % count;
            var candidate = await (from f in db.Fragments
                join c in db.Candidates on f.Id equals c.FragmentId
                where c.LangPair == "ba-ru"
                orderby f.TextBa
                select new { FragmentId = f.Id, CandidateId = c.Id }).Skip(offset).FirstOrDefaultAsync();
            if (candidate is null) return null;
            db.DailyTasks.Add(new DailyTask { UserId = userId, FragmentId = candidate.FragmentId,
                CandidateId = candidate.CandidateId, LangPair = "ba-ru", TaskDate = date });
            try { await db.SaveChangesAsync(); }
            // A concurrent request may have inserted the task first; reload it below.
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { db.ChangeTracker.Clear(); }
        }
        var task = await (from t in db.DailyTasks.AsNoTracking()
            join f in db.Fragments.AsNoTracking() on t.FragmentId equals f.Id
            join c in db.Candidates.AsNoTracking() on t.CandidateId equals c.Id
            join s in db.Sources.AsNoTracking() on f.SourceId equals s.Id
            where t.UserId == userId && t.TaskDate == date && t.LangPair == "ba-ru"
            select new { t.Id, t.TaskDate, t.LangPair, f.TextBa, Candidate = c.Text,
                s.Title, s.Dataset, s.OriginalId, s.SourceUrl, s.Attribution, s.License }).FirstOrDefaultAsync();
        if (task is null) return null;
        var submission = await db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.TaskId == task.Id);
        return new Dictionary<string, object?> {
            ["id"] = task.Id, ["task_date"] = task.TaskDate, ["lang_pair"] = task.LangPair,
            ["text_ba"] = task.TextBa, ["candidate"] = task.Candidate, ["source_title"] = task.Title,
            ["dataset"] = task.Dataset, ["original_id"] = task.OriginalId, ["source_url"] = task.SourceUrl,
            ["attribution"] = task.Attribution, ["license"] = task.License,
            ["submitted_action"] = submission?.Action, ["submitted_text"] = submission?.AnswerText
        };
    }

    /// <summary>Stores one answer per task, awards points once, and updates consensus.</summary>
    public async Task<ServiceResult> Submit(Guid userId, Guid taskId, Submission input)
    {
        if (input.Action is not ("confirm" or "correct")) return ServiceResult.BadRequest(new { error = "Действие: confirm или correct." });
        var task = await db.DailyTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return ServiceResult.NotFound();
        var candidate = await db.Candidates.AsNoTracking().FirstAsync(c => c.Id == task.CandidateId);
        var answer = input.Action == "confirm" ? candidate.Text : input.AnswerText?.Trim() ?? "";
        if (answer.Length is < 1 or > 1000) return ServiceResult.BadRequest(new { error = "Введите перевод длиной от 1 до 1000 символов." });
        // The submission, points, and consensus record must commit together.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.TaskId == taskId);
        if (submission is null)
        {
            submission = new UserSubmission { TaskId = taskId, UserId = userId, FragmentId = task.FragmentId,
                LangPair = task.LangPair, Action = input.Action, AnswerText = answer };
            db.Submissions.Add(submission);
        }
        else { submission.Action = input.Action; submission.AnswerText = answer; submission.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        // Editing an earlier answer does not award the task's points again.
        if (!await db.PointEvents.AnyAsync(p => p.SubmissionId == submission.Id))
        {
            db.PointEvents.Add(new PointEvent { UserId = userId, SubmissionId = submission.Id, Points = 10 });
            await db.SaveChangesAsync();
        }
        // Count only the latest answer from each user; imported candidates are not votes.
        var submissions = await db.Submissions.AsNoTracking().Where(s => s.FragmentId == task.FragmentId && s.LangPair == task.LangPair)
            .Select(s => new { s.Id, s.UserId, s.AnswerText, s.UpdatedAt }).ToListAsync();
        var answers = submissions.GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id).First().AnswerText);
        var votes = answers.GroupBy(a => a.Trim().ToLowerInvariant())
            .Select(g => new { Normalized = g.Key, Example = g.Min(), Count = g.Count() })
            .OrderByDescending(v => v.Count).ThenBy(v => v.Normalized).ToList();
        var top = votes[0];
        var total = votes.Sum(v => v.Count);
        // Two matching users are required, and a tie keeps the translation disputed.
        var status = top.Count >= 2 && (votes.Count == 1 || top.Count > votes[1].Count) ? "confirmed" : total >= 2 ? "disputed" : "collecting";
        var translation = await db.Translations.FirstOrDefaultAsync(d => d.FragmentId == task.FragmentId && d.LangPair == task.LangPair);
        if (translation is null)
        {
            var origin = await (from fragment in db.Fragments.AsNoTracking()
                join source in db.Sources.AsNoTracking() on fragment.SourceId equals source.Id
                where fragment.Id == task.FragmentId
                select new { fragment.TextBa, source.Title, source.SourceUrl, source.Dataset, source.OriginalId }).FirstAsync();
            // The translation row keeps a snapshot of the fragment and source coordinates.
            translation = new TranslationRecord { FragmentId = task.FragmentId, LangPair = task.LangPair,
                TextBa = origin.TextBa,
                Source = JsonSerializer.Serialize(new { document = origin.Title, file = origin.SourceUrl,
                    chapter = (string?)null, time = (string?)null, dataset = origin.Dataset, original_id = origin.OriginalId }) };
            db.Translations.Add(translation);
        }
        translation.Translation = status == "confirmed" ? top.Example : null;
        translation.Status = status;
        translation.Confirmed = top.Count;
        translation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Success(new { status, confirmations = top.Count, points = 10, answer });
    }
}
