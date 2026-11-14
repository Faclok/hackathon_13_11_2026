using System.Text.Json;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Database is required");
builder.Services.AddDbContext<LmsDbContext>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Lms")));
builder.Services.AddHttpClient("main", client => client.BaseAddress = new Uri(builder.Configuration["MAIN_API_URL"] ?? "http://main-api:8080"));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
    await db.Database.MigrateAsync();
    await DemoSeed.Apply(db);
}

app.MapGet("/api/lms/health", async (LmsDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapGet("/api/lms/courses", async (LmsDbContext db) =>
{
    var courses = await db.Courses.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    var counts = await db.Lessons.AsNoTracking().GroupBy(l => l.CourseId)
        .Select(g => new { CourseId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.CourseId, g => g.Count);
    return Results.Ok(courses.Select(c => new { id = c.Id, title = c.Title, description = c.Description,
        lesson_count = counts.GetValueOrDefault(c.Id) }));
});
app.MapGet("/api/lms/courses/{id:int}/lessons", async (int id, LmsDbContext db) => Results.Ok(
    await db.Lessons.AsNoTracking().Where(l => l.CourseId == id).OrderBy(l => l.Position)
        .Select(l => new { id = l.Id, course_id = l.CourseId, position = l.Position, title = l.Title, body = l.Body }).ToListAsync()));

app.MapGet("/api/lms/me/progress", async (HttpRequest request, IHttpClientFactory http, LmsDbContext db) =>
{
    var user = await Resolve(request, http);
    return user is null ? Results.Unauthorized() : Results.Ok(await db.Progress.AsNoTracking()
        .Where(p => p.UserId == user).OrderBy(p => p.LessonId)
        .Select(p => new { lesson_id = p.LessonId, completed_at = p.CompletedAt }).ToListAsync());
});
app.MapPost("/api/lms/lessons/{id:int}/complete", async (int id, HttpRequest request, IHttpClientFactory http, LmsDbContext db) =>
{
    var user = await Resolve(request, http);
    if (user is null) return Results.Unauthorized();
    if (!await db.Lessons.AnyAsync(l => l.Id == id)) return Results.NotFound();
    if (!await db.Progress.AnyAsync(p => p.UserId == user && p.LessonId == id))
    {
        db.Progress.Add(new LmsProgress { UserId = user.Value, LessonId = id });
        await db.SaveChangesAsync();
    }
    return Results.Ok(new { completed = true, lessonId = id });
});

app.MapTeacherEndpoints();

app.Run();

static async Task<Guid?> Resolve(HttpRequest request, IHttpClientFactory factory)
{
    var bearer = request.Headers.Authorization.ToString();
    if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    using var message = new HttpRequestMessage(HttpMethod.Get, "/api/me");
    message.Headers.TryAddWithoutValidation("Authorization", bearer);
    using var response = await factory.CreateClient("main").SendAsync(message);
    if (!response.IsSuccessStatusCode) return null;
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("id").GetGuid();
}
