using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

static class Db
{
    public static async Task<Dictionary<string, object?>?> One(NpgsqlDataSource db, string sql, params (string key, object? value)[] args)
        => (await All(db, sql, args)).FirstOrDefault();

    public static async Task<List<Dictionary<string, object?>>> All(NpgsqlDataSource db, string sql, params (string key, object? value)[] args)
    {
        await using var cmd = db.CreateCommand(sql);
        foreach (var (key, value) in args)
        {
            if (value is null) cmd.Parameters.AddWithValue(key, NpgsqlDbType.Text, DBNull.Value);
            else cmd.Parameters.AddWithValue(key, value);
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}

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
    public static async Task<object> Create(NpgsqlDataSource db, Dictionary<string, object?> user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await Db.One(db, "INSERT INTO sessions(token_hash,user_id,expires_at) VALUES (@hash,@user,now()+interval '30 days') RETURNING user_id", ("hash", Sha(token)), ("user", user["id"]));
        return new { token, user };
    }

    public static Task<Guid?> User(HttpRequest request, NpgsqlDataSource db) => User(request.Headers.Authorization.ToString(), db);

    public static async Task<Guid?> User(string authorization, NpgsqlDataSource db)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        if (token.Length != 64) return null;
        var row = await Db.One(db, "SELECT user_id FROM sessions WHERE token_hash=@hash AND expires_at>now()", ("hash", Sha(token)));
        return row?["user_id"] as Guid?;
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
    public static async Task<Dictionary<string, object?>?> Today(NpgsqlDataSource db, Guid userId)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var countRow = await Db.One(db, "SELECT COUNT(*)::int AS count FROM fragments");
        var count = (int)countRow!["count"]!;
        if (count == 0) return null;
        var offset = date.DayNumber % count;
        await Db.One(db, "INSERT INTO daily_tasks(user_id,fragment_id,candidate_id,lang_pair,task_date) SELECT @user,f.id,c.id,'ba-ru',@date FROM fragments f JOIN translation_candidates c ON c.fragment_id=f.id AND c.lang_pair='ba-ru' ORDER BY f.text_ba OFFSET @offset LIMIT 1 ON CONFLICT(user_id,lang_pair,task_date) DO NOTHING RETURNING id", ("user", userId), ("date", date), ("offset", offset));
        return await Db.One(db, "SELECT t.id,t.task_date,t.lang_pair,f.text_ba,c.text AS candidate,s.title AS source_title,s.dataset,s.original_id,s.source_url,s.attribution,s.license,sub.action AS submitted_action,sub.answer_text AS submitted_text FROM daily_tasks t JOIN fragments f ON f.id=t.fragment_id JOIN translation_candidates c ON c.id=t.candidate_id JOIN source_items s ON s.id=f.source_id LEFT JOIN submissions sub ON sub.task_id=t.id WHERE t.user_id=@user AND t.task_date=@date AND t.lang_pair='ba-ru'", ("user", userId), ("date", date));
    }

    public static async Task<IResult> Submit(NpgsqlDataSource db, Guid userId, Guid taskId, Submission input)
    {
        if (input.Action is not ("confirm" or "correct")) return Results.BadRequest(new { error = "Действие: confirm или correct." });
        var task = await Db.One(db, "SELECT t.fragment_id,t.lang_pair,c.text AS candidate FROM daily_tasks t JOIN translation_candidates c ON c.id=t.candidate_id WHERE t.id=@task AND t.user_id=@user", ("task", taskId), ("user", userId));
        if (task is null) return Results.NotFound();
        var answer = input.Action == "confirm" ? (string)task["candidate"]! : input.AnswerText?.Trim() ?? "";
        if (answer.Length is < 1 or > 1000) return Results.BadRequest(new { error = "Введите перевод длиной от 1 до 1000 символов." });
        var submission = await Db.One(db, "INSERT INTO submissions(task_id,user_id,fragment_id,lang_pair,action,answer_text) VALUES (@task,@user,@fragment,@pair,@action,@answer) ON CONFLICT(task_id) DO UPDATE SET action=EXCLUDED.action,answer_text=EXCLUDED.answer_text,updated_at=now() RETURNING id", ("task", taskId), ("user", userId), ("fragment", task["fragment_id"]), ("pair", task["lang_pair"]), ("action", input.Action), ("answer", answer));
        await Db.One(db, "INSERT INTO point_events(user_id,submission_id,points) VALUES (@user,@submission,10) ON CONFLICT(submission_id) DO NOTHING RETURNING id", ("user", userId), ("submission", submission!["id"]));

        // Only independent user answers count. Imported candidates are never votes.
        var votes = await Db.All(db, "SELECT lower(btrim(answer_text)) AS normalized,MIN(answer_text) AS example,COUNT(*)::int AS count FROM submissions WHERE fragment_id=@fragment AND lang_pair=@pair GROUP BY lower(btrim(answer_text)) ORDER BY count DESC,normalized", ("fragment", task["fragment_id"]), ("pair", task["lang_pair"]));
        var top = votes[0];
        var best = (int)top["count"]!;
        var total = votes.Sum(v => (int)v["count"]!);
        var status = best >= 2 && (votes.Count == 1 || best > (int)votes[1]["count"]!) ? "confirmed" : total >= 2 ? "disputed" : "collecting";
        var translation = status == "confirmed" ? top["example"] : null;
        var decision = await Db.One(db, "INSERT INTO consensus_decisions(fragment_id,lang_pair,translation,status,confirmations) VALUES (@fragment,@pair,@translation,@status,@count) ON CONFLICT(fragment_id,lang_pair) DO UPDATE SET translation=EXCLUDED.translation,status=EXCLUDED.status,confirmations=EXCLUDED.confirmations,updated_at=now() RETURNING status,confirmations", ("fragment", task["fragment_id"]), ("pair", task["lang_pair"]), ("translation", translation), ("status", status), ("count", best));
        return Results.Ok(new { status, confirmations = decision!["confirmations"], points = 10, answer });
    }
}
