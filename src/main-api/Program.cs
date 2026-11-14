using System.Security.Cryptography;
using System.Text;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required");
var internalKey = builder.Configuration["INTERNAL_API_KEY"] ?? throw new InvalidOperationException("INTERNAL_API_KEY is required");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
var app = builder.Build();

app.MapGet("/api/health", async (NpgsqlDataSource db) =>
{
    try { await Db.One(db, "SELECT 1 AS ok"); return Results.Ok(new { status = "ok" }); }
    catch { return Results.StatusCode(503); }
});

app.MapPost("/api/auth/register", async (Credentials input, NpgsqlDataSource db) =>
{
    var username = input.Username?.Trim().ToLowerInvariant() ?? "";
    if (username.Length is < 3 or > 32 || input.Password is null || input.Password.Length < 8 || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 80)
        return Results.BadRequest(new { error = "Укажите логин (3–32), имя и пароль (не менее 8 символов)." });
    var passwordHash = Passwords.Hash(input.Password);
    try
    {
        var user = await Db.One(db, "INSERT INTO app_users(username,password_hash,display_name) VALUES (@username,@password,@name) RETURNING id,display_name,lang_pair", ("username", username), ("password", passwordHash), ("name", input.DisplayName.Trim()));
        return Results.Ok(await Sessions.Create(db, user!));
    }
    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
    { return Results.Conflict(new { error = "Этот логин уже занят." }); }
});

app.MapPost("/api/auth/login", async (Login input, NpgsqlDataSource db) =>
{
    var user = await Db.One(db, "SELECT id,display_name,lang_pair,password_hash FROM app_users WHERE username=@username", ("username", input.Username?.Trim().ToLowerInvariant() ?? ""));
    if (user is null || user["password_hash"] is not string hash || !Passwords.Verify(input.Password ?? "", hash)) return Results.Unauthorized();
    user.Remove("password_hash");
    return Results.Ok(await Sessions.Create(db, user));
});

app.MapGet("/api/me", async (HttpRequest request, NpgsqlDataSource db) =>
{
    var user = await Sessions.User(request, db);
    if (user is null) return Results.Unauthorized();
    var profile = await Db.One(db, "SELECT u.id,u.display_name,u.username,u.telegram_id,u.lang_pair,COALESCE(SUM(p.points),0)::int AS points FROM app_users u LEFT JOIN point_events p ON p.user_id=u.id WHERE u.id=@id GROUP BY u.id", ("id", user));
    return Results.Ok(profile);
});

app.MapGet("/api/tasks/today", async (HttpRequest request, NpgsqlDataSource db) =>
{
    var user = await Sessions.User(request, db);
    return user is null ? Results.Unauthorized() : Results.Ok(await Tasks.Today(db, user.Value));
});

app.MapPost("/api/tasks/{id:guid}/submission", async (Guid id, Submission input, HttpRequest request, NpgsqlDataSource db) =>
{
    var user = await Sessions.User(request, db);
    return user is null ? Results.Unauthorized() : await Tasks.Submit(db, user.Value, id, input);
});

app.MapGet("/api/leaderboard", async (NpgsqlDataSource db) => Results.Ok(await Db.All(db,
    "SELECT u.display_name,COALESCE(SUM(p.points),0)::int AS points,COUNT(p.id)::int AS answers FROM app_users u JOIN point_events p ON p.user_id=u.id GROUP BY u.id ORDER BY points DESC,answers DESC,u.display_name LIMIT 20")));

app.MapGet("/api/translations", async (NpgsqlDataSource db) => Results.Ok(await Db.All(db,
    "SELECT f.id AS fragment_id,f.text_ba,d.lang_pair,d.translation,d.confirmations,d.status,s.title AS source_title,s.dataset,s.original_id,s.source_url,s.attribution,s.license FROM consensus_decisions d JOIN fragments f ON f.id=d.fragment_id JOIN source_items s ON s.id=f.source_id WHERE d.status='confirmed' ORDER BY f.text_ba")));

app.MapGet("/api/translations/export", async (NpgsqlDataSource db) =>
{
    var rows = await Db.All(db, "SELECT f.id AS fragment_id,f.text_ba,d.lang_pair,d.translation,d.confirmations,d.status,s.dataset,s.original_id,s.source_url FROM consensus_decisions d JOIN fragments f ON f.id=d.fragment_id JOIN source_items s ON s.id=f.source_id WHERE d.status='confirmed' ORDER BY f.text_ba");
    var names = new[] { "fragment_id", "text_ba", "lang_pair", "translation", "confirmations", "status", "dataset", "original_id", "source_url" };
    var csv = new StringBuilder(string.Join(',', names) + "\r\n");
    foreach (var row in rows) csv.AppendLine(string.Join(',', names.Select(n => "\"" + (row[n]?.ToString() ?? "").Replace("\"", "\"\"") + "\"")));
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "translations.csv");
});

app.MapGet("/api/internal/resolve-token", async (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await Sessions.User(request, db);
    return user is null ? Results.Unauthorized() : Results.Ok(new { userId = user });
});

app.MapPost("/api/internal/telegram/{telegramId:long}/start", async (long telegramId, TelegramStart input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var name = string.IsNullOrWhiteSpace(input.Name) ? "Участник" : input.Name.Trim()[..Math.Min(input.Name.Trim().Length, 80)];
    var user = await Db.One(db, "INSERT INTO app_users(telegram_id,display_name) VALUES (@id,@name) ON CONFLICT(telegram_id) DO UPDATE SET display_name=EXCLUDED.display_name RETURNING id,display_name", ("id", telegramId), ("name", name));
    await Db.One(db, "INSERT INTO bot_states(telegram_id,state) VALUES (@id,'idle') ON CONFLICT(telegram_id) DO UPDATE SET state='idle',task_id=NULL,updated_at=now() RETURNING state", ("id", telegramId));
    return Results.Ok(user);
});

app.MapGet("/api/internal/telegram/{telegramId:long}/task", async (long telegramId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await Db.One(db, "SELECT id FROM app_users WHERE telegram_id=@id", ("id", telegramId));
    if (user is null) return Results.NotFound();
    var task = await Tasks.Today(db, (Guid)user["id"]!);
    await Db.One(db, "INSERT INTO bot_states(telegram_id,state,task_id) VALUES (@id,'awaiting_answer',@task) ON CONFLICT(telegram_id) DO UPDATE SET state='awaiting_answer',task_id=@task,updated_at=now() RETURNING state", ("id", telegramId), ("task", task!["id"]!));
    return Results.Ok(task);
});

app.MapPost("/api/internal/telegram/{telegramId:long}/submit", async (long telegramId, Submission input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var state = await Db.One(db, "SELECT u.id AS user_id,b.task_id,b.state FROM app_users u JOIN bot_states b ON b.telegram_id=u.telegram_id WHERE u.telegram_id=@id", ("id", telegramId));
    if (state is null || (string)state["state"]! != "awaiting_answer" || state["task_id"] is not Guid taskId) return Results.BadRequest(new { error = "Сначала запросите задание командой /task." });
    var result = await Tasks.Submit(db, (Guid)state["user_id"]!, taskId, input);
    if (result is IStatusCodeHttpResult { StatusCode: 200 })
        await Db.One(db, "UPDATE bot_states SET state='idle',task_id=NULL,updated_at=now() WHERE telegram_id=@id RETURNING state", ("id", telegramId));
    return result;
});

app.MapPost("/api/internal/telegram/{telegramId:long}/cancel", async (long telegramId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    await Db.One(db, "INSERT INTO bot_states(telegram_id,state) VALUES (@id,'idle') ON CONFLICT(telegram_id) DO UPDATE SET state='idle',task_id=NULL,updated_at=now() RETURNING state", ("id", telegramId));
    return Results.Ok();
});

app.MapGet("/api/internal/telegram/{telegramId:long}/me", async (long telegramId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await Db.One(db, "SELECT u.display_name,COALESCE(SUM(p.points),0)::int AS points FROM app_users u LEFT JOIN point_events p ON p.user_id=u.id WHERE u.telegram_id=@id GROUP BY u.id", ("id", telegramId));
    return user is null ? Results.NotFound() : Results.Ok(user);
});

app.Run();

record Credentials(string? Username, string? Password, string? DisplayName);
record Login(string? Username, string? Password);
record Submission(string? Action, string? AnswerText);
record TelegramStart(string? Name);
