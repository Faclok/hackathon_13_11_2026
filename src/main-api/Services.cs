using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

static class Sessions
{
    public static async Task<object> Create(AppDbContext db, AppUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        db.Sessions.Add(new UserSession { TokenHash = Sha(token), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        await db.SaveChangesAsync();
        return new { token, user = new { id = user.Id, display_name = user.DisplayName, lang_pair = user.LangPair } };
    }

    public static Task<Guid?> User(HttpRequest request, AppDbContext db) => User(request.Headers.Authorization.ToString(), db);

    public static async Task<Guid?> User(string authorization, AppDbContext db)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        if (token.Length != 64) return null;
        return await db.Sessions.AsNoTracking().Where(s => s.TokenHash == Sha(token) && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => (Guid?)s.UserId).FirstOrDefaultAsync();
    }

    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

static class Internal
{
    public static bool Valid(HttpRequest request, string expected)
    {
        var actual = request.Headers["X-Internal-Key"].ToString();
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }
}

static class Tasks
{
    public static async Task<Dictionary<string, object?>?> Today(AppDbContext db, Guid userId)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var existing = await db.DailyTasks.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.LangPair == "ba-ru" && t.TaskDate == date);
        if (existing is null)
        {
            var count = await db.Fragments.CountAsync();
            if (count == 0) return null;
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

    public static async Task<IResult> Submit(AppDbContext db, Guid userId, Guid taskId, Submission input)
    {
        if (input.Action is not ("confirm" or "correct")) return Results.BadRequest(new { error = "Действие: confirm или correct." });
        var task = await db.DailyTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);
        if (task is null) return Results.NotFound();
        var candidate = await db.Candidates.AsNoTracking().FirstAsync(c => c.Id == task.CandidateId);
        var answer = input.Action == "confirm" ? candidate.Text : input.AnswerText?.Trim() ?? "";
        if (answer.Length is < 1 or > 1000) return Results.BadRequest(new { error = "Введите перевод длиной от 1 до 1000 символов." });
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
        if (!await db.PointEvents.AnyAsync(p => p.SubmissionId == submission.Id))
        {
            db.PointEvents.Add(new PointEvent { UserId = userId, SubmissionId = submission.Id, Points = 10 });
            await db.SaveChangesAsync();
        }
        // Only independent user answers count. Imported candidates are never votes.
        var answers = await db.Submissions.AsNoTracking().Where(s => s.FragmentId == task.FragmentId && s.LangPair == task.LangPair)
            .Select(s => s.AnswerText).ToListAsync();
        var votes = answers.GroupBy(a => a.Trim().ToLowerInvariant())
            .Select(g => new { Normalized = g.Key, Example = g.Min(), Count = g.Count() })
            .OrderByDescending(v => v.Count).ThenBy(v => v.Normalized).ToList();
        var top = votes[0];
        var total = votes.Sum(v => v.Count);
        var status = top.Count >= 2 && (votes.Count == 1 || top.Count > votes[1].Count) ? "confirmed" : total >= 2 ? "disputed" : "collecting";
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.FragmentId == task.FragmentId && d.LangPair == task.LangPair);
        if (decision is null)
        {
            decision = new ConsensusDecision { FragmentId = task.FragmentId, LangPair = task.LangPair };
            db.Decisions.Add(decision);
        }
        decision.Translation = status == "confirmed" ? top.Example : null;
        decision.Status = status;
        decision.Confirmations = top.Count;
        decision.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { status, confirmations = top.Count, points = 10, answer });
    }
}
