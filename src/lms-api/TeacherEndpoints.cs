using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public static class TeacherEndpoints
{
    public static void MapTeacherEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/lms");

        api.MapGet("/teacher/me", async (HttpRequest request, IHttpClientFactory factory) =>
        {
            var user = await Identity(request, factory);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });
        api.MapGet("/teacher/classes", async (HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await db.TeacherClasses.AsNoTracking().Where(x => x.TeacherId == user.Id)
                .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync());
        });
        api.MapPost("/teacher/classes", async (HttpRequest request, IHttpClientFactory factory, LmsDbContext db, ClassInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (Clean(input.Name, 200) is not { } name || input.Description?.Length > 2000)
                return Bad("Укажите название класса до 200 символов.");
            var item = new TeacherClass { TeacherId = user.Id, Name = name, Description = input.Description?.Trim() ?? "" };
            db.TeacherClasses.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lms/teacher/classes/{item.Id}", item);
        });
        api.MapGet("/teacher/classes/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await OwnClass(db, id, user.Id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        api.MapPut("/teacher/classes/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, ClassInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await OwnClass(db, id, user.Id);
            if (item is null) return Results.NotFound();
            if (Clean(input.Name, 200) is not { } name || input.Description?.Length > 2000)
                return Bad("Укажите название класса до 200 символов.");
            item.Name = name; item.Description = input.Description?.Trim() ?? "";
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });
        api.MapDelete("/teacher/classes/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await OwnClass(db, id, user.Id);
            if (item is null) return Results.NotFound();
            db.TeacherClasses.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/teacher/classes/{id:guid}/members", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            return Results.Ok(await db.ClassMembers.AsNoTracking().Where(x => x.ClassId == id).OrderBy(x => x.Username).ToListAsync());
        });
        api.MapPost("/teacher/classes/{id:guid}/members", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, MemberInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            if (Clean(input.Username, 100) is not { } username) return Bad("Укажите логин участника.");
            using var response = await MainGet(request, factory, $"/api/lms-source/participants/lookup?username={Uri.EscapeDataString(username)}");
            if (!response.IsSuccessStatusCode) return response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? Bad("Участник с таким логином не найден.") : Results.StatusCode(502);
            var participant = await response.Content.ReadFromJsonAsync<LmsUser>();
            if (participant is null || participant.Id == user.Id) return Bad("Нельзя добавить себя в класс.");
            if (await db.ClassMembers.AnyAsync(x => x.ClassId == id && x.UserId == participant.Id))
                return Results.Conflict(new { title = "Участник уже в классе." });
            var member = new ClassMember { ClassId = id, UserId = participant.Id, Username = participant.Username, DisplayName = participant.Username };
            db.ClassMembers.Add(member);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lms/teacher/classes/{id}/members", member);
        });
        api.MapGet("/teacher/classes/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var member = await db.ClassMembers.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == id && x.UserId == memberId);
            return member is null ? Results.NotFound() : Results.Ok(member);
        });
        api.MapPut("/teacher/classes/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, MemberEditInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var member = await db.ClassMembers.SingleOrDefaultAsync(x => x.ClassId == id && x.UserId == memberId);
            if (member is null) return Results.NotFound();
            if (Clean(input.DisplayName, 200) is not { } displayName) return Bad("Укажите имя участника до 200 символов.");
            member.DisplayName = displayName;
            await db.SaveChangesAsync();
            return Results.Ok(member);
        });
        api.MapDelete("/teacher/classes/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var member = await db.ClassMembers.SingleOrDefaultAsync(x => x.ClassId == id && x.UserId == memberId);
            if (member is null) return Results.NotFound();
            db.ClassMembers.Remove(member);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/teacher/translations", async (HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await db.TeacherTranslations.AsNoTracking().Where(x => x.TeacherId == user.Id)
                .OrderByDescending(x => x.CreatedAt).ToListAsync());
        });
        api.MapPost("/teacher/translations", async (HttpRequest request, IHttpClientFactory factory, LmsDbContext db, TranslationInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (Clean(input.SourceText, 5000) is not { } source || Clean(input.TranslationText, 5000) is not { } translation ||
                input.LanguagePair is not ("ba-ru" or "ru-ba")) return Bad("Заполните предложение, перевод и направление перевода.");
            var item = new TeacherTranslation { TeacherId = user.Id, SourceText = source, TranslationText = translation, LanguagePair = input.LanguagePair };
            db.TeacherTranslations.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lms/teacher/translations/{item.Id}", item);
        });
        api.MapGet("/teacher/translations/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await db.TeacherTranslations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == user.Id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        api.MapPut("/teacher/translations/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, TranslationInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await db.TeacherTranslations.SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == user.Id);
            if (item is null) return Results.NotFound();
            if (Clean(input.SourceText, 5000) is not { } source || Clean(input.TranslationText, 5000) is not { } translation ||
                input.LanguagePair is not ("ba-ru" or "ru-ba")) return Bad("Заполните предложение, перевод и направление перевода.");
            item.SourceText = source; item.TranslationText = translation; item.LanguagePair = input.LanguagePair;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });
        api.MapDelete("/teacher/translations/{id:guid}", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            var item = await db.TeacherTranslations.SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == user.Id);
            if (item is null) return Results.NotFound();
            db.TeacherTranslations.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/teacher/catalog", async (int? offset, int? limit, HttpRequest request, IHttpClientFactory factory) =>
        {
            if (await Identity(request, factory) is null) return Results.Unauthorized();
            using var response = await MainGet(request, factory, $"/api/lms-source/translations?offset={offset ?? 0}&limit={limit ?? 50}");
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            return Results.Content(await response.Content.ReadAsStringAsync(), "application/json");
        });

        api.MapGet("/teacher/classes/{id:guid}/tasks", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            return Results.Ok(await db.ClassTasks.AsNoTracking().Where(x => x.ClassId == id).OrderByDescending(x => x.CreatedAt).ToListAsync());
        });
        api.MapPost("/teacher/classes/{id:guid}/tasks", async (Guid id, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, TaskInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var (task, error) = await BuildTask(input, user.Id, request, factory, db);
            if (error is not null) return Bad(error);
            task!.ClassId = id;
            db.ClassTasks.Add(task);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lms/teacher/classes/{id}/tasks/{task.Id}", task);
        });
        api.MapGet("/teacher/classes/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var item = await db.ClassTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == taskId && x.ClassId == id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        api.MapPut("/teacher/classes/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, TaskInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var item = await db.ClassTasks.SingleOrDefaultAsync(x => x.Id == taskId && x.ClassId == id);
            if (item is null) return Results.NotFound();
            if (await db.TaskSubmissions.AnyAsync(x => x.TaskId == taskId)) return Results.Conflict(new { title = "Задание уже выполнено. Создайте новое." });
            var (updated, error) = await BuildTask(input, user.Id, request, factory, db);
            if (error is not null) return Bad(error);
            item.Title = updated!.Title; item.DueAt = updated.DueAt; item.SourceText = updated.SourceText;
            item.ReferenceTranslation = updated.ReferenceTranslation; item.TranslationStatus = updated.TranslationStatus;
            item.TranslationRating = updated.TranslationRating; item.CatalogCaseId = updated.CatalogCaseId;
            item.TeacherTranslationId = updated.TeacherTranslationId;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });
        api.MapDelete("/teacher/classes/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null) return Results.NotFound();
            var item = await db.ClassTasks.SingleOrDefaultAsync(x => x.Id == taskId && x.ClassId == id);
            if (item is null) return Results.NotFound();
            db.ClassTasks.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/teacher/classes/{id:guid}/tasks/{taskId:guid}/submissions", async (Guid id, Guid taskId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (await OwnClass(db, id, user.Id) is null || !await db.ClassTasks.AnyAsync(x => x.Id == taskId && x.ClassId == id)) return Results.NotFound();
            return Results.Ok(await (from answer in db.TaskSubmissions.AsNoTracking() where answer.TaskId == taskId
                join member in db.ClassMembers on new { ClassId = id, answer.UserId } equals new { member.ClassId, member.UserId } into members
                from member in members.DefaultIfEmpty()
                orderby answer.SubmittedAt descending
                select new { answer.UserId, Username = member == null ? "Участник удалён" : member.DisplayName, answer.Answer, answer.SubmittedAt }).ToListAsync());
        });
        api.MapGet("/me/tasks", async (HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await (from member in db.ClassMembers.AsNoTracking() where member.UserId == user.Id
                join studyClass in db.TeacherClasses on member.ClassId equals studyClass.Id
                join task in db.ClassTasks on studyClass.Id equals task.ClassId
                join submission in db.TaskSubmissions.Where(x => x.UserId == user.Id) on task.Id equals submission.TaskId into answers
                from submission in answers.DefaultIfEmpty()
                orderby task.CreatedAt descending
                select new { task.Id, task.ClassId, ClassName = studyClass.Name, task.Title, task.SourceText,
                    task.DueAt, task.CreatedAt, Answer = submission == null ? null : submission.Answer,
                    SubmittedAt = submission == null ? (DateTimeOffset?)null : submission.SubmittedAt }).ToListAsync());
        });
        api.MapPut("/me/tasks/{taskId:guid}/submission", async (Guid taskId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db, SubmissionInput input) =>
        {
            var user = await Identity(request, factory);
            if (user is null) return Results.Unauthorized();
            if (Clean(input.Answer, 5000) is not { } answer) return Bad("Напишите перевод до 5000 символов.");
            var task = await db.ClassTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == taskId);
            if (task is null || !await db.ClassMembers.AnyAsync(x => x.ClassId == task.ClassId && x.UserId == user.Id)) return Results.NotFound();
            if (task.DueAt is not null && task.DueAt < DateTimeOffset.UtcNow) return Results.Conflict(new { title = "Срок сдачи истёк." });
            var submission = await db.TaskSubmissions.SingleOrDefaultAsync(x => x.TaskId == taskId && x.UserId == user.Id);
            if (submission is null) { submission = new TaskSubmission { TaskId = taskId, UserId = user.Id }; db.TaskSubmissions.Add(submission); }
            submission.Answer = answer; submission.SubmittedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(submission);
        });
    }

    private static async Task<(ClassTask?, string?)> BuildTask(TaskInput input, Guid teacherId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db)
    {
        if (Clean(input.Title, 200) is not { } title) return (null, "Укажите название задания.");
        var task = new ClassTask { Title = title, DueAt = input.DueAt };
        if (input.SourceType == "own")
        {
            var translation = await db.TeacherTranslations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.TranslationId && x.TeacherId == teacherId);
            if (translation is null) return (null, "Свой перевод не найден.");
            task.TeacherTranslationId = translation.Id; task.SourceText = translation.SourceText;
            task.ReferenceTranslation = translation.TranslationText; task.TranslationStatus = "own";
        }
        else if (input.SourceType == "catalog")
        {
            using var response = await MainGet(request, factory, $"/api/lms-source/translations/{input.TranslationId}");
            if (!response.IsSuccessStatusCode) return (null, "Перевод из каталога не найден.");
            var translation = await response.Content.ReadFromJsonAsync<CatalogTranslation>();
            if (translation is null) return (null, "Не удалось прочитать перевод.");
            task.CatalogCaseId = translation.Id; task.SourceText = translation.SourceText;
            task.ReferenceTranslation = translation.TranslationText;
            task.TranslationStatus = translation.Status; task.TranslationRating = translation.Rating;
        }
        else return (null, "Выберите свой перевод или перевод из каталога.");
        return (task, null);
    }

    private static Task<TeacherClass?> OwnClass(LmsDbContext db, Guid id, Guid teacherId) =>
        db.TeacherClasses.SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == teacherId);
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? null : value.Trim();
    private static IResult Bad(string title) => Results.BadRequest(new { title });
    private static async Task<LmsUser?> Identity(HttpRequest request, IHttpClientFactory factory)
    {
        using var response = await MainGet(request, factory, "/api/me");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<LmsUser>() : null;
    }
    private static Task<HttpResponseMessage> MainGet(HttpRequest request, IHttpClientFactory factory, string path)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.ToString());
        return factory.CreateClient("main").SendAsync(message);
    }
}

public sealed record LmsUser(Guid Id, string Username);
public sealed record ClassInput(string Name, string? Description);
public sealed record MemberInput(string Username);
public sealed record MemberEditInput(string DisplayName);
public sealed record TranslationInput(string SourceText, string TranslationText, string LanguagePair);
public sealed record TaskInput(string Title, string SourceType, Guid TranslationId, DateTimeOffset? DueAt);
public sealed record SubmissionInput(string Answer);
public sealed record CatalogTranslation(Guid Id, string SourceText, string TranslationText, string LanguagePair, int Rating, string Status);
