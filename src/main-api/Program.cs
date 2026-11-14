using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required");
var internalKey = builder.Configuration["INTERNAL_API_KEY"] ?? throw new InvalidOperationException("INTERNAL_API_KEY is required");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Main")));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DemoSeed.Apply(db);
}

app.MapGet("/api/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));

app.MapPost("/api/auth/register", async (Credentials input, AppDbContext db) =>
{
    var username = input.Username?.Trim().ToLowerInvariant() ?? "";
    if (username.Length is < 3 or > 32 || input.Password is null || input.Password.Length < 8 || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 80)
        return Results.BadRequest(new { error = "Укажите логин (3–32), имя и пароль (не менее 8 символов)." });
    var user = new AppUser { Username = username, PasswordHash = Passwords.Hash(input.Password), DisplayName = input.DisplayName.Trim() };
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); return Results.Ok(await Sessions.Create(db, user)); }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    { return Results.Conflict(new { error = "Этот логин уже занят." }); }
});

app.MapPost("/api/auth/login", async (Login input, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == (input.Username ?? "").Trim().ToLower());
    if (user?.PasswordHash is not string hash || !Passwords.Verify(input.Password ?? "", hash)) return Results.Unauthorized();
    return Results.Ok(await Sessions.Create(db, user));
});

app.MapGet("/api/me", async (HttpRequest request, AppDbContext db) =>
{
    var userId = await Sessions.User(request, db);
    if (userId is null) return Results.Unauthorized();
    var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
    var points = await db.PointEvents.Where(p => p.UserId == userId).SumAsync(p => (int?)p.Points) ?? 0;
    return Results.Ok(new { id = user.Id, display_name = user.DisplayName, username = user.Username,
        telegram_id = user.TelegramId, lang_pair = user.LangPair, points });
});

app.MapGet("/api/tasks/today", async (HttpRequest request, AppDbContext db) =>
{
    var userId = await Sessions.User(request, db);
    return userId is null ? Results.Unauthorized() : Results.Ok(await Tasks.Today(db, userId.Value));
});

app.MapPost("/api/tasks/{id:guid}/submission", async (Guid id, Submission input, HttpRequest request, AppDbContext db) =>
{
    var userId = await Sessions.User(request, db);
    return userId is null ? Results.Unauthorized() : await Tasks.Submit(db, userId.Value, id, input);
});

app.MapGet("/api/leaderboard", async (AppDbContext db) =>
{
    var scores = await db.PointEvents.AsNoTracking().GroupBy(p => p.UserId)
        .Select(g => new { UserId = g.Key, Points = g.Sum(p => p.Points), Answers = g.Count() }).ToListAsync();
    var users = await db.Users.AsNoTracking().Where(u => scores.Select(s => s.UserId).Contains(u.Id))
        .Select(u => new { u.Id, u.DisplayName }).ToListAsync();
    return Results.Ok((from s in scores join u in users on s.UserId equals u.Id
        orderby s.Points descending, s.Answers descending, u.DisplayName
        select new { display_name = u.DisplayName, points = s.Points, answers = s.Answers }).Take(20));
});

app.MapGet("/api/translations", async (AppDbContext db) =>
{
    var rows = await (from d in db.Decisions.AsNoTracking()
        join f in db.Fragments.AsNoTracking() on d.FragmentId equals f.Id
        join s in db.Sources.AsNoTracking() on f.SourceId equals s.Id
        where d.Status == "confirmed"
        orderby f.TextBa
        select new { fragment_id = f.Id, text_ba = f.TextBa, lang_pair = d.LangPair,
            translation = d.Translation, confirmations = d.Confirmations, status = d.Status,
            source_title = s.Title, dataset = s.Dataset, original_id = s.OriginalId,
            source_url = s.SourceUrl, attribution = s.Attribution, license = s.License }).ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/api/translations/export", async (AppDbContext db) =>
{
    var rows = await (from d in db.Decisions.AsNoTracking()
        join f in db.Fragments.AsNoTracking() on d.FragmentId equals f.Id
        join s in db.Sources.AsNoTracking() on f.SourceId equals s.Id
        where d.Status == "confirmed"
        orderby f.TextBa
        select new { fragment_id = f.Id, text_ba = f.TextBa, lang_pair = d.LangPair,
            translation = d.Translation, confirmations = d.Confirmations, status = d.Status,
            dataset = s.Dataset, original_id = s.OriginalId, source_url = s.SourceUrl }).ToListAsync();
    var names = new[] { "fragment_id", "text_ba", "lang_pair", "translation", "confirmations", "status", "dataset", "original_id", "source_url" };
    var csv = new StringBuilder(string.Join(',', names) + "\r\n");
    foreach (var row in rows)
    {
        var values = new object?[] { row.fragment_id, row.text_ba, row.lang_pair, row.translation, row.confirmations,
            row.status, row.dataset, row.original_id, row.source_url };
        csv.AppendLine(string.Join(',', values.Select(value => "\"" + (value?.ToString() ?? "").Replace("\"", "\"\"") + "\"")));
    }
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "translations.csv");
});

app.MapGet("/api/internal/resolve-token", async (HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var userId = await Sessions.User(request, db);
    return userId is null ? Results.Unauthorized() : Results.Ok(new { userId });
});

app.MapPost("/api/internal/telegram/{telegramId:long}/start", async (long telegramId, TelegramStart input, HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var name = string.IsNullOrWhiteSpace(input.Name) ? "Участник" : input.Name.Trim()[..Math.Min(input.Name.Trim().Length, 80)];
    var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
    if (user is null) { user = new AppUser { TelegramId = telegramId, DisplayName = name }; db.Users.Add(user); }
    else user.DisplayName = name;
    await db.SaveChangesAsync();
    return Results.Ok(new { id = user.Id, display_name = user.DisplayName });
});

app.MapGet("/api/internal/telegram/{telegramId:long}/task", async (long telegramId, HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
    if (user is null) return Results.NotFound();
    var task = await Tasks.Today(db, user.Id);
    if (task is null) return Results.NotFound();
    return Results.Ok(task);
});

app.MapPost("/api/internal/telegram/{telegramId:long}/submit", async (long telegramId, Submission input, HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
    if (user is null || input.TaskId is not Guid taskId)
        return Results.BadRequest(new { error = "Сначала запросите задание командой /task." });
    return await Tasks.Submit(db, user.Id, taskId, input);
});

app.MapPost("/api/internal/telegram/{telegramId:long}/cancel", async (long telegramId, HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    return Results.Ok();
});

app.MapGet("/api/internal/telegram/{telegramId:long}/me", async (long telegramId, HttpRequest request, AppDbContext db) =>
{
    if (!Internal.Valid(request, internalKey)) return Results.Unauthorized();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
    if (user is null) return Results.NotFound();
    var points = await db.PointEvents.Where(p => p.UserId == user.Id).SumAsync(p => (int?)p.Points) ?? 0;
    return Results.Ok(new { display_name = user.DisplayName, points });
});

app.Run();

record Credentials(string? Username, string? Password, string? DisplayName);
record Login(string? Username, string? Password);
record Submission(string? Action, string? AnswerText, Guid? TaskId = null);
record TelegramStart(string? Name);
