using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Database is required")));
builder.Services.AddHttpClient("main", client => client.BaseAddress = new Uri(builder.Configuration["MAIN_API_URL"] ?? "http://main-api:8080"));
var internalKey = builder.Configuration["INTERNAL_API_KEY"] ?? throw new InvalidOperationException("INTERNAL_API_KEY is required");
var app = builder.Build();

app.MapGet("/api/lms/health", async (NpgsqlDataSource db) =>
{
    try { await LmsDb.Query(db, "SELECT 1"); return Results.Ok(new { status = "ok" }); }
    catch { return Results.StatusCode(503); }
});
app.MapGet("/api/lms/courses", async (NpgsqlDataSource db) => Results.Ok(await LmsDb.Query(db,
    "SELECT c.id,c.title,c.description,COUNT(l.id)::int AS lesson_count FROM lms_courses c LEFT JOIN lms_lessons l ON l.course_id=c.id GROUP BY c.id ORDER BY c.id")));
app.MapGet("/api/lms/courses/{id:int}/lessons", async (int id, NpgsqlDataSource db) => Results.Ok(await LmsDb.Query(db,
    "SELECT id,course_id,position,title,body FROM lms_lessons WHERE course_id=@id ORDER BY position", ("id", id))));

app.MapGet("/api/lms/me/progress", async (HttpRequest request, IHttpClientFactory http, NpgsqlDataSource db) =>
{
    var user = await Resolve(request, http, internalKey);
    return user is null ? Results.Unauthorized() : Results.Ok(await LmsDb.Query(db,
        "SELECT lesson_id,completed_at FROM lms_progress WHERE user_id=@user ORDER BY lesson_id", ("user", user)));
});
app.MapPost("/api/lms/lessons/{id:int}/complete", async (int id, HttpRequest request, IHttpClientFactory http, NpgsqlDataSource db) =>
{
    var user = await Resolve(request, http, internalKey);
    if (user is null) return Results.Unauthorized();
    var lesson = await LmsDb.Query(db, "SELECT id FROM lms_lessons WHERE id=@id", ("id", id));
    if (lesson.Count == 0) return Results.NotFound();
    await LmsDb.Query(db, "INSERT INTO lms_progress(user_id,lesson_id) VALUES (@user,@lesson) ON CONFLICT(user_id,lesson_id) DO UPDATE SET completed_at=lms_progress.completed_at RETURNING lesson_id,completed_at", ("user", user), ("lesson", id));
    return Results.Ok(new { completed = true, lessonId = id });
});

app.Run();

static async Task<Guid?> Resolve(HttpRequest request, IHttpClientFactory factory, string key)
{
    var bearer = request.Headers.Authorization.ToString();
    if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    using var message = new HttpRequestMessage(HttpMethod.Get, "/api/internal/resolve-token");
    message.Headers.TryAddWithoutValidation("Authorization", bearer);
    message.Headers.Add("X-Internal-Key", key);
    using var response = await factory.CreateClient("main").SendAsync(message);
    if (!response.IsSuccessStatusCode) return null;
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("userId").GetGuid();
}

static class LmsDb
{
    public static async Task<List<Dictionary<string, object?>>> Query(NpgsqlDataSource db, string sql, params (string key, object value)[] args)
    {
        await using var command = db.CreateCommand(sql);
        foreach (var (key, value) in args) command.Parameters.AddWithValue(key, value);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }
}
