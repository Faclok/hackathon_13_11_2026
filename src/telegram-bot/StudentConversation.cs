using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotKit;
using TelegramBotKit.Fallbacks;
using TelegramBotKit.Messaging;

public sealed class StudentConversation(HttpClient api, IDbContextFactory<BotDbContext> factory, ExerciseAudio audio,
    IConfiguration? configuration = null, ILogger<StudentConversation>? logger = null) : IDefaultMessageHandler
{
    private string Portal => configuration?["STUDENT_PORTAL_URL"] ?? "https://bashperevod.ru/profile#telegram";
    private string Site => configuration?["MINI_APP_URL"] ?? "https://bashperevod.ru";
    private BotContext context = null!;
    private BotDbContext db = null!;
    private BotState state = null!;
    private long chatId;
    private long userId;
    private CancellationToken Ct => context.CancellationToken;
    private string Prefix => $"/internal/telegram/{userId}";

    public Task StartAsync(Message message, BotContext ctx) => Run(ctx, async () =>
    {
        var parts = (message.Text ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            using var response = await api.PostAsJsonAsync(Prefix + "/link", new { code = parts[1] }, Ct);
            if (!response.IsSuccessStatusCode) { await Say("Ссылка недействительна или уже использована. Получите новую в профиле:\n" + Portal); return; }
            await Say("Telegram привязан. Теперь доступны уровни, босс и сайт внутри Telegram.");
        }
        await ShowMenu();
    });
    public Task MenuAsync(BotContext ctx) => Run(ctx, ShowMenu);
    public Task DailyAsync(BotContext ctx) => Run(ctx, Next);
    public Task TasksAsync(BotContext ctx) => Run(ctx, ShowKinds);
    public Task BossAsync(BotContext ctx) => Run(ctx, Boss);
    public Task WebsiteAsync(BotContext ctx) => Run(ctx, () => Say("Рейтинг и весь сайт откроются внутри Telegram с вашим аккаунтом.", TaskButtons.Website(Site)));
    public Task ProfileAsync(BotContext ctx) => Run(ctx, ShowProfile);
    private async Task ShowProfile()
    {
        var profile = await Get<Profile>(Prefix + "/profile");
        var progress = await Get<LearningProgress>(Prefix + "/learning/progress");
        await Say($"Ваш профиль\nЛогин: {profile.Username}\nШкола: {profile.SchoolName ?? "не указана"}\nКласс: {profile.ClassName ?? "не указан"}\n\nПройдено уровней: {progress.CompletedLevels}\nБоссов пройдено: {progress.CompletedBosses}\nДоступно боссов: {progress.AvailableBosses}\nПопыток: {progress.Attempts}, ошибок: {progress.Mistakes}");
    }
    public Task NavigateAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, () => args.FirstOrDefault() switch
    {
        "daily" or "level" => Next(), "boss" => Boss(), "profile" => ShowProfile(), "retry" => Retry(), "kinds" => ShowKinds(), _ => ShowMenu()
    });
    // Old type-selection buttons now resume the server-selected current level.
    public Task IssueAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, Next);

    public Task ReviewAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (args.Length != 1 || args[0] is not ("translation-choice" or "translation-confirm" or "translation-create")) { await ShowKinds(); return; }
        using var response = await api.PostAsJsonAsync(Prefix + "/reviews/next?kind=" + args[0], new { }, Ct);
        Check(response);
        if (response.StatusCode == HttpStatusCode.NoContent) { await Say("Сейчас нет переводов для проверки. Можно продолжить уровень.", TaskButtons.Kinds()); return; }
        var review = (await response.Content.ReadFromJsonAsync<ReviewView>(Ct))!;
        await SaveReview(new(review));
        await Say(ReviewChat.Prompt(review), TaskButtons.Review(review));
    });
    public Task ReviewAnswerAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (state.State.StartsWith("sending:")) { await Retry(); return; }
        if (!MatchesTask(args) || !state.State.StartsWith("review:")) { await Stale(); return; }
        var draft = JsonSerializer.Deserialize<ReviewDraft>(state.State[7..])!;
        var action = args[1];
        if (action == "custom" && draft.Assignment.AllowCustom)
        {
            await SaveReview(draft with { Custom = true });
            await Say("Напишите свой перевод одним сообщением."); return;
        }
        ReviewAnswer answer;
        if (action == "skip") answer = new("Skip");
        else if (action == "confirm" && draft.Assignment.Kind == "translation-confirm") answer = new("Confirm");
        else if (action.StartsWith('v') && int.TryParse(action[1..], out var index) && draft.Assignment.Kind == "translation-choice" && index >= 0 && index < draft.Assignment.Options.Length)
            answer = new("Confirm", SelectedVariantId: draft.Assignment.Options[index].Id);
        else { await Stale(); return; }
        await SubmitReview(draft, answer);
    });
    public Task ExerciseAnswerAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (state.State.StartsWith("sending:")) { await Retry(); return; }
        if (!MatchesTask(args) || !state.State.StartsWith("exercise:")) { await Stale(); return; }
        var draft = JsonSerializer.Deserialize<ExerciseDraft>(state.State[9..])!;
        if (draft.LastUpdateId >= context.Update.Id) return;
        var value = args[1];
        if (value == "send") { await SubmitExercise(draft, TaskButtons.Answer(draft)); return; }
        if (value.StartsWith('g') && draft.Exercise.Kind == "missing-word" && int.TryParse(value[1..], out var index))
        { await SubmitExercise(draft, ExerciseChat.Answer(draft.Exercise, (index + 1).ToString())); return; }
        var selected = TaskButtons.Select(draft, value) with { LastUpdateId = context.Update.Id };
        await SaveExercise(selected);
        await context.Sender.EditText(query, new EditText
        { Text = ExerciseChat.Prompt(selected.Exercise) + TaskButtons.Progress(selected), ReplyMarkup = TaskButtons.Exercise(selected) }, Ct);
    });
    public Task HandleAsync(Message message, BotContext ctx) => Run(ctx, async () =>
    {
        if (state.State.StartsWith("sending:")) { await Retry(); return; }
        var text = message.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) { await Say("Отправьте ответ текстом или используйте кнопки задания."); return; }
        if (state.State.StartsWith("review:"))
        {
            var draft = JsonSerializer.Deserialize<ReviewDraft>(state.State[7..])!;
            await SubmitReview(draft, ReviewChat.Answer(draft.Assignment, text, draft.Custom)); return;
        }
        if (state.State.StartsWith("exercise:"))
        {
            var draft = JsonSerializer.Deserialize<ExerciseDraft>(state.State[9..])!;
            await SubmitExercise(draft, ExerciseChat.Answer(draft.Exercise, text)); return;
        }
        await Say("Откройте текущий уровень командой /level или босса командой /boss.");
    });
    private async Task Run(BotContext ctx, Func<Task> action)
    {
        var message = ctx.Update.Message ?? ctx.Update.CallbackQuery?.Message;
        var user = ctx.Update.CallbackQuery?.From ?? message?.From;
        if (message?.Chat.Type != ChatType.Private || user is null || user.IsBot || user.Id != message.Chat.Id) return;
        context = ctx; chatId = message.Chat.Id; userId = user.Id;
        if (ctx.Update.CallbackQuery is { } callback) await ctx.Sender.AnswerCallback(callback.Id, new AnswerCallback(), Ct);
        await using var currentDb = await factory.CreateDbContextAsync(Ct);
        db = currentDb;
        state = await db.States.SingleOrDefaultAsync(x => x.TelegramId == userId, Ct) ?? new() { TelegramId = userId };
        if (db.Entry(state).State == EntityState.Detached) db.States.Add(state);
        // A delivered answer must never become an answer to the following question.
        if (state.AnswerMessageId >= ctx.Update.Id && !state.State.StartsWith("sending:")) return;
        try { if (state.State.StartsWith("sending:")) await Retry(); else await action(); }
        catch (StudentApiException ex) { await Reset(); await Say(ex.Message); }
        catch (FormatException) { await Say("Проверьте ответ. Используйте кнопки текущего задания или /level."); }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning("Student API request failed with status {Status}", ex.StatusCode);
            await Say("Сервис временно недоступен. Сохранённый ответ можно повторить без новой попытки.", TaskButtons.Continue("retry", "Повторить"));
        }
        catch (TaskCanceledException) when (!Ct.IsCancellationRequested)
        { await Say("Сервис не успел ответить. Повторите сохранённое действие.", TaskButtons.Continue("retry", "Повторить")); }
    }
    private async Task SubmitExercise(ExerciseDraft draft, object answer)
    {
        if (draft.LevelId is null || draft.AttemptToken is null) { await Next(); return; }
        await Send(new(Prefix + $"/learning/levels/{draft.LevelId}/questions/{draft.Exercise.Id}/answer",
            JsonSerializer.SerializeToElement(new { attemptToken = draft.AttemptToken, answer }), "level", context.Update.Id));
    }
    private Task SubmitReview(ReviewDraft draft, ReviewAnswer answer) => Send(new(
        draft.BossId is Guid bossId ? Prefix + $"/learning/bosses/{bossId}/questions/{draft.Assignment.Id}/answer" : Prefix + $"/reviews/{draft.Assignment.Id}/response",
        JsonSerializer.SerializeToElement(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)), draft.BossId.HasValue ? "boss" : "review", context.Update.Id));
    private async Task Send(PendingBotAnswer pending)
    {
        await Save("sending:" + JsonSerializer.Serialize(pending), null);
        using var response = await api.PostAsJsonAsync(pending.Path, pending.Body, Ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await Reset(); await Say("Задание изменилось, возможно, вы уже ответили на сайте. Откройте его заново.", TaskButtons.Continue(pending.Mode == "boss" ? "boss" : "level")); return;
        }
        Check(response);
        state.AnswerMessageId = Math.Max(pending.UpdateId, context.Update.Id);
        if (pending.Mode == "level")
        {
            var result = (await response.Content.ReadFromJsonAsync<LearningAnswer>(Ct))!;
            await Save("level-result", null);
            await Say(result.LevelCompleted ? "Уровень пройден! Следующий можно начать по кнопке. Босс доступен за каждые пять уровней." :
                result.Recognized ? "Верно! Ответ сохранён." : "Пока неверно. Этот вопрос вернётся в конце уровня.", TaskButtons.Continue("level", result.LevelCompleted ? "Следующий уровень" : "Продолжить уровень"));
        }
        else if (pending.Mode == "boss")
        {
            var result = (await response.Content.ReadFromJsonAsync<BossAnswer>(Ct))!;
            await Save("boss-result", null);
            await Say(result.BossCompleted ? "Босс пройден! Все три перевода сохранены для согласования." : "Ответ сохранён для согласования. Продолжим босса?", TaskButtons.Continue(result.BossCompleted ? "level" : "boss"));
        }
        else { await Reset(); await Say("Ваш ответ сохранён.", TaskButtons.Kinds()); }
    }
    private Task Retry() => state.State.StartsWith("sending:")
        ? Send(JsonSerializer.Deserialize<PendingBotAnswer>(state.State[8..])!) : state.State == "boss-loading" ? Boss() : Next();
    private bool MatchesTask(string[] args) => args.Length == 2 && Guid.TryParseExact(args[0], "N", out var id) && state.TaskId == id;
    private Task Stale() => Say("Эта кнопка относится к предыдущему заданию. Откройте текущий уровень или босса.");
    private Task SaveExercise(ExerciseDraft draft) => Save("exercise:" + JsonSerializer.Serialize(draft), draft.AttemptToken ?? draft.Exercise.Id);
    private Task SaveReview(ReviewDraft draft) => Save("review:" + JsonSerializer.Serialize(draft), draft.Assignment.Id);
    private Task Reset() => Save("idle", null);
    private async Task Save(string value, Guid? id)
    { state.State = value; state.TaskId = id; state.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(Ct); }
    private async Task ShowMenu()
    {
        await Get<Profile>(Prefix + "/profile");
        await Say("Главное меню", new ReplyKeyboardRemove());
        await Say("Продолжайте текущий уровень или пройдите босса. Рейтинг и весь сайт доступны как Mini App без повторного входа.");
    }
    private Task ShowKinds() => Say("Вопросы уровня подбирает сервер. Можно продолжить уровень или помочь с переводами.", TaskButtons.Kinds());
    private async Task Next()
    {
        if (state.State.StartsWith("sending:")) { await Retry(); return; }
        using var response = await api.PostAsJsonAsync(Prefix + "/learning/current", new { }, Ct);
        Check(response);
        if (response.StatusCode == HttpStatusCode.NoContent) { await Reset(); await Say("Для нового уровня пока нет материалов. Попробуйте позже."); return; }
        var level = (await response.Content.ReadFromJsonAsync<LearningLevel>(Ct))!;
        var q = level.Question;
        var exercise = new ExerciseView(q.Id, "", false, q.Kind, q.SchemaVersion, q.Prompt);
        var draft = new ExerciseDraft(exercise, [], LevelId: level.Id, AttemptToken: q.AttemptToken);
        await SaveExercise(draft);
        await Say($"Уровень {level.Number} · выполнено {level.CorrectCount}/{level.QuestionCount}\n\n" + ExerciseChat.Prompt(exercise) + TaskButtons.Progress(draft), TaskButtons.Exercise(draft));
        if (q.Kind is "audio-dictation" or "audio-translation" && q.Prompt.TryGetProperty("audioUrl", out var url) && url.GetString() is { Length: > 0 } link)
            await audio.SendAsync(context.BotClient, chatId, link, Ct);
    }
    private async Task Boss()
    {
        if (state.State.StartsWith("sending:")) { await Retry(); return; }
        await Save("boss-loading", null);
        using var response = await api.PostAsJsonAsync(Prefix + "/learning/boss", new { }, Ct);
        Check(response);
        if (response.StatusCode == HttpStatusCode.NoContent) { await Say("Босс открывается за каждые пять пройденных уровней. Пока можно продолжить обучение.", TaskButtons.Continue("level")); return; }
        var boss = (await response.Content.ReadFromJsonAsync<LearningBoss>(Ct))!;
        if (boss.Question is null) { await Reset(); await Say($"Босс: сохранено {boss.CompletedCount}/{boss.QuestionCount}. Новых переводов пока нет. Попробуйте позже.", TaskButtons.Continue("boss")); return; }
        await SaveReview(new(boss.Question, Boss: true, BossId: boss.Id));
        await Say($"👑 Босс за {boss.Checkpoint} уровней · {boss.CompletedCount}/{boss.QuestionCount}\n\n" + ReviewChat.Prompt(boss.Question), TaskButtons.Review(boss.Question, boss: true));
    }
    private async Task Say(string value, ReplyMarkup? keyboard = null)
    {
        for (var start = 0; start < value.Length;)
        {
            var length = Math.Min(4000, value.Length - start);
            if (char.IsHighSurrogate(value[start + length - 1])) length--;
            await context.Sender.SendText(chatId, new SendText { Text = value.Substring(start, length), ReplyMarkup = keyboard ?? TaskButtons.Menu(Site) }, Ct);
            start += length;
        }
    }
    private async Task<T> Get<T>(string url)
    { using var response = await api.GetAsync(url, Ct); Check(response); return (await response.Content.ReadFromJsonAsync<T>(Ct))!; }
    private void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) throw new StudentApiException("Откройте сайт и нажмите «Авторизоваться в Telegram» в профиле. Если аккаунт уже привязан, откройте задание заново:\n" + Portal);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new StudentApiException("Проверьте привязку и доступ к аккаунту на сайте:\n" + Portal);
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new StudentApiException("Ответ не принят или выдача истекла. Откройте текущий уровень или босса заново.");
        if (response.StatusCode == HttpStatusCode.Conflict) throw new StudentApiException("Состояние изменилось. Повторите открытие текущего уровня или босса.");
        response.EnsureSuccessStatusCode();
    }
    private sealed class StudentApiException(string message) : Exception(message);
    private sealed record Profile(Guid Id, string Username, Guid? SchoolId, Guid? ClassId, string? SchoolName, string? ClassName);
}
public sealed record ExerciseView(Guid Id, string Original, bool Completed, string Kind, int SchemaVersion, JsonElement Prompt);
public sealed record LearningQuestion(Guid Id, Guid AttemptToken, string Kind, int SchemaVersion, JsonElement Prompt);
public sealed record LearningLevel(Guid Id, long Number, int QuestionCount, int CorrectCount, LearningQuestion Question);
public sealed record LearningProgress(long CompletedLevels, long CompletedBosses, long AvailableBosses, long Attempts, long Mistakes);
public sealed record LearningAnswer(Guid AttemptToken, bool Recognized, bool LevelCompleted);
public sealed record LearningBoss(Guid Id, long Checkpoint, int QuestionCount, int CompletedCount, ReviewView? Question);
public sealed record BossAnswer(Guid ResponseId, bool Archived, bool BossCompleted);
public sealed record PendingBotAnswer(string Path, JsonElement Body, string Mode, int UpdateId);
