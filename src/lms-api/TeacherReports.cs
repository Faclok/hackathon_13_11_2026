using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;

public static class TeacherReports
{
    public static void MapTeacherReports(this WebApplication app)
    {
        var api = app.MapGroup("/api/lms/teacher/classes/{classId:guid}");
        api.MapGet("/report", async (Guid classId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var actor = await Identity(request, factory);
            if (actor is null) return Results.Unauthorized();
            ClassReport? report;
            try { report = await BuildReport(classId, actor.Id, db, request, factory); }
            catch (HttpRequestException) { return Results.Problem(statusCode: 502, title: "Нет связи с сервисом решений переводов."); }
            return report is null ? Results.NotFound() : Results.Ok(report);
        });
        api.MapGet("/tasks/export", async (Guid classId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var actor = await Identity(request, factory);
            if (actor is null) return Results.Unauthorized();
            ClassReport? report;
            try { report = await BuildReport(classId, actor.Id, db, request, factory); }
            catch (HttpRequestException) { return Results.Problem(statusCode: 502, title: "Нет связи с сервисом решений переводов."); }
            if (report is null) return Results.NotFound();
            var lines = new List<string> { Csv("Класс", "Задание", "Предложение", "Решённый / предложенный вариант", "Основание решения", "Поддержка варианта", "Срок сдачи", "Выдано") };
            lines.AddRange(report.Tasks.Select(task => Csv(report.ClassName, task.Title, task.SourceText,
                task.AcceptedTranslations.Count > 0 ? string.Join("; ", task.AcceptedTranslations) : task.ReferenceTranslation,
                task.TranslationStatus, task.TranslationRating.ToString(),
                Format(task.DueAt), Format(task.CreatedAt))));
            return CsvFile(lines, $"class-{classId}-tasks.csv");
        });
        api.MapGet("/report/export", async (Guid classId, HttpRequest request, IHttpClientFactory factory, LmsDbContext db) =>
        {
            var actor = await Identity(request, factory);
            if (actor is null) return Results.Unauthorized();
            ClassReport? report;
            try { report = await BuildReport(classId, actor.Id, db, request, factory); }
            catch (HttpRequestException) { return Results.Problem(statusCode: 502, title: "Нет связи с сервисом решений переводов."); }
            if (report is null) return Results.NotFound();
            var lines = new List<string> { Csv("Класс", "Задание", "Ученик", "Логин", "Участие", "Оценка", "Предложение", "Ответ ученика", "Решённый / предложенный вариант", "Основание решения", "Поддержка варианта", "Дата ответа") };
            foreach (var task in report.Tasks)
                lines.AddRange(task.Answers.Select(answer => Csv(report.ClassName, task.Title, answer.DisplayName,
                    answer.Username, answer.Answer is null ? "Не сдал" : "Сдал", VerdictLabel(answer.Verdict), task.SourceText,
                    answer.Answer ?? "", answer.ReferenceTranslation, task.TranslationStatus, task.TranslationRating.ToString(),
                    Format(answer.SubmittedAt))));
            return CsvFile(lines, $"class-{classId}-report.csv");
        });
    }

    public static string Verdict(TaskSubmission? submission, string status, IEnumerable<string> acceptedTranslations)
    {
        if (submission is null) return "missing";
        if (status is not ("community" or "expert")) return "review";
        return acceptedTranslations.Any(reference => SameTranslation(submission.Answer, reference)) ? "correct" : "incorrect";
    }

    public static bool SameTranslation(string answer, string reference)
    {
        static string Normalize(string text)
        {
            var chars = text.Normalize(NormalizationForm.FormC).ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
            return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return Normalize(answer) == Normalize(reference);
    }

    private static async Task<ClassReport?> BuildReport(Guid classId, Guid teacherId, LmsDbContext db,
        HttpRequest request, IHttpClientFactory factory)
    {
        var studyClass = await db.TeacherClasses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId && x.TeacherId == teacherId);
        if (studyClass is null) return null;
        var members = await db.ClassMembers.Where(x => x.ClassId == classId).OrderBy(x => x.DisplayName).ToArrayAsync();
        var currentNames = await TeacherEndpoints.CurrentNames(request, factory, members.Select(x => x.UserId));
        foreach (var member in members)
            if (currentNames.TryGetValue(member.UserId, out var name) && member.DisplayName != name) member.DisplayName = name;
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        var tasks = await db.ClassTasks.AsNoTracking().Where(x => x.ClassId == classId).OrderByDescending(x => x.CreatedAt).ToArrayAsync();
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var submissions = await db.TaskSubmissions.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToArrayAsync();
        var byTaskAndUser = submissions.ToDictionary(x => (x.TaskId, x.UserId));
        var taskReports = new List<TaskReport>();
        foreach (var task in tasks)
        {
            CatalogTranslation? translation = null;
            if (task.CatalogCaseId is Guid caseId)
            {
                using var response = await MainGet(request, factory, $"/api/lms-source/translations/{caseId}");
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("Не удалось получить актуальное решение перевода.");
                translation = await response.Content.ReadFromJsonAsync<CatalogTranslation>();
            }
            var status = translation?.Status ?? "pending";
            var reference = translation?.TranslationText ?? task.ReferenceTranslation;
            var accepted = translation?.AcceptedTranslations ?? [];
            if (status is "community" or "expert" && accepted.Length == 0)
                throw new HttpRequestException("Решение перевода не содержит принятых вариантов.");
            var answers = members.Select(member =>
            {
                byTaskAndUser.TryGetValue((task.Id, member.UserId), out var submission);
                var matching = submission is null ? null : accepted.FirstOrDefault(x => SameTranslation(submission.Answer, x));
                return new AnswerReport(member.UserId, member.Username,
                    currentNames.GetValueOrDefault(member.UserId, member.DisplayName),
                    submission?.Answer, submission?.SubmittedAt, Verdict(submission, status, accepted), matching ?? reference);
            }).ToArray();
            taskReports.Add(new TaskReport(task.Id, task.Title, task.SourceText, reference,
                status, translation?.Rating ?? 0, task.DueAt, task.CreatedAt,
                answers.Count(x => x.Answer is not null), answers.Count(x => x.Verdict == "correct"),
                answers.Count(x => x.Verdict == "incorrect"), answers.Count(x => x.Verdict == "review"), accepted, answers));
        }
        return new ClassReport(studyClass.Id, studyClass.Name, members.Length, taskReports);
    }

    public static string Csv(params string?[] values) => string.Join(',', values.Select(value =>
    {
        var cell = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        if (cell.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') cell = "'" + cell;
        return '"' + cell.Replace("\"", "\"\"") + '"';
    }));
    private static string Format(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "";
    private static string VerdictLabel(string verdict) => verdict switch
    {
        "correct" => "Верно", "incorrect" => "Ошибка", "review" => "Нужна проверка", _ => "Не сдал"
    };
    private static IResult CsvFile(IEnumerable<string> lines, string fileName)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }
    private static async Task<LmsUser?> Identity(HttpRequest request, IHttpClientFactory factory)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        message.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.ToString());
        using var response = await factory.CreateClient("main").SendAsync(message);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<LmsUser>() : null;
    }
    private static Task<HttpResponseMessage> MainGet(HttpRequest request, IHttpClientFactory factory, string path)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.ToString());
        return factory.CreateClient("main").SendAsync(message);
    }
}

public sealed record AnswerReport(Guid UserId, string Username, string DisplayName, string? Answer,
    DateTimeOffset? SubmittedAt, string Verdict, string ReferenceTranslation);
public sealed record TaskReport(Guid Id, string Title, string SourceText, string ReferenceTranslation,
    string TranslationStatus, int TranslationRating, DateTimeOffset? DueAt, DateTimeOffset CreatedAt,
    int Participated, int Correct, int Incorrect, int NeedsReview, IReadOnlyList<string> AcceptedTranslations,
    IReadOnlyList<AnswerReport> Answers);
public sealed record ClassReport(Guid ClassId, string ClassName, int MemberCount, IReadOnlyList<TaskReport> Tasks);
