using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotKit;
using TelegramBotKit.Fallbacks;
using TelegramBotKit.Messaging;

public sealed class StudentConversation(HttpClient api, IDbContextFactory<BotDbContext> factory, ExerciseAudio audio, IConfiguration? configuration = null) : IDefaultMessageHandler
{
    private string Portal => configuration?["STUDENT_PORTAL_URL"] ?? Environment.GetEnvironmentVariable("STUDENT_PORTAL_URL") ?? "https://bashperevod.ru/telegram.html";
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
            if (!response.IsSuccessStatusCode) { await Say("Ссылка недействительна или уже использована. Получите новую:\n" + Portal); return; }
            await Reset();
            await Say("Telegram привязан. Здесь доступны ваш профиль и ежедневные задания.");
        }
        else await ShowMenu();
    });

    public Task MenuAsync(BotContext ctx) => Run(ctx, ShowMenu);
    public Task ProfileAsync(BotContext ctx) => Run(ctx, async () =>
    {
        var profile = await Get<Profile>(Prefix + "/profile");
        await Reset();
        await Say($"Ваш профиль\nЛогин: {profile.Username}\nШкола: {profile.SchoolName ?? "не указана"}\nКласс: {profile.ClassName ?? "не указан"}");
    });
    public Task DailyAsync(BotContext ctx) => Run(ctx, Next);
    public Task TasksAsync(BotContext ctx) => Run(ctx, ShowKinds);

    public Task NavigateAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, () => args.FirstOrDefault() switch
    {
        "daily" => Next(), "kinds" => ShowKinds(), _ => Say("Откройте /tasks и выберите задание.")
    });

    public Task IssueAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (args.Length != 1 || !ExerciseChat.Kinds.Values.Contains(args[0])) { await ShowKinds(); return; }
        using var response = await api.PostAsJsonAsync(Prefix + "/lesson/exercises", new { kind = args[0], languagePair = "ba-ru" }, Ct);
        Check(response);
        if (response.StatusCode == HttpStatusCode.NoContent) { await Say("Для этого вида пока нет подходящих материалов. Выберите другое задание.", TaskButtons.Kinds()); return; }
        var exercise = (await response.Content.ReadFromJsonAsync<ExerciseView>(Ct))!;
        if (exercise.Completed) { await Say("Это упражнение сегодня уже выполнено. Выберите другой вид или продолжите урок.", TaskButtons.Kinds()); return; }
        await ShowExercise(exercise);
    });

    public Task ReviewAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (args.Length != 1 || args[0] is not ("translation-choice" or "translation-confirm")) { await ShowKinds(); return; }
        using var response = await api.PostAsJsonAsync(Prefix + "/reviews/next?kind=" + args[0], new { }, Ct);
        Check(response);
        if (response.StatusCode == HttpStatusCode.NoContent) { await Say("Сейчас нет доступных переводов для проверки. Можно пройти учебные упражнения.", TaskButtons.Kinds()); return; }
        var review = (await response.Content.ReadFromJsonAsync<ReviewView>(Ct))!;
        await SaveReview(new(review));
        await Say(ReviewChat.Prompt(review), TaskButtons.Review(review));
    });

    public Task ReviewAnswerAsync(CallbackQuery query, string[] args, BotContext ctx) => Run(ctx, async () =>
    {
        if (!MatchesTask(args) || !state.State.StartsWith("review:", StringComparison.Ordinal)) { await Stale(); return; }
        var draft = JsonSerializer.Deserialize<ReviewDraft>(state.State[7..])!;
        var action = args[1];
        if (action == "custom" && draft.Assignment.AllowCustom)
        {
            await SaveReview(draft with { Custom = true });
            await Say("Напишите свой перевод одним сообщением. /cancel — вернуться в меню.");
            return;
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
        if (!MatchesTask(args) || !state.State.StartsWith("exercise:", StringComparison.Ordinal)) { await Stale(); return; }
        var draft = ReadExercise();
        if (draft.LastUpdateId == context.Update.Id)
        {
            await Say(ExerciseChat.Prompt(draft.Exercise) + TaskButtons.Progress(draft), TaskButtons.Exercise(draft));
            return;
        }
        var value = args[1];
        if (value == "send") { await SubmitExercise(draft.Exercise, TaskButtons.Answer(draft)); return; }
        if (value.StartsWith('g') && draft.Exercise.Kind == "missing-word" && int.TryParse(value[1..], out var index))
        {
            await SubmitExercise(draft.Exercise, ExerciseChat.Answer(draft.Exercise, (index + 1).ToString()));
            return;
        }
        var selected = TaskButtons.Select(draft, value) with { LastUpdateId = context.Update.Id };
        await SaveExercise(selected);
        await context.Sender.EditText(query, new EditText
        {
            Text = ExerciseChat.Prompt(selected.Exercise) + TaskButtons.Progress(selected), ReplyMarkup = TaskButtons.Exercise(selected)
        }, Ct);
    });

    // Fallback owns free text only; slash, text, and callback commands are owned by TelegramBotKit.
    public Task HandleAsync(Message message, BotContext ctx) => Run(ctx, async () =>
    {
        var text = message.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) { await Say("Отправьте ответ текстом или воспользуйтесь кнопками задания."); return; }
        if (state.State.StartsWith("review:", StringComparison.Ordinal))
        {
            var draft = JsonSerializer.Deserialize<ReviewDraft>(state.State[7..])!;
            await SubmitReview(draft, ReviewChat.Answer(draft.Assignment, text, draft.Custom));
            return;
        }
        if (state.TaskId is not Guid id || !(state.State.StartsWith("exercise:", StringComparison.Ordinal) || state.State == "awaiting_answer"))
        { await Say("Выберите задание через /tasks или продолжите урок командой /daily."); return; }
        // Bind redelivered text to its original exercise across a process restart.
        if (state.AnswerMessageId == context.Update.Id && state.AnswerExerciseId is Guid prior) id = prior;
        var lesson = await Get<Lesson>(Prefix + "/lesson");
        var exercise = lesson.Exercises.SingleOrDefault(x => x.Id == id && !x.Completed);
        if (exercise is null) { await Next(); return; }
        await SubmitExercise(exercise, ExerciseChat.Answer(exercise, text));
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
        try { await action(); }
        catch (StudentApiException ex) { await Reset(); await Say(ex.Message, TaskButtons.Kinds()); }
        catch (FormatException) { await Say("Проверьте выбор. Используйте кнопки текущего задания или откройте /tasks."); }
        catch (HttpRequestException) { await Say("Сервис временно недоступен. Повторите действие чуть позже."); }
        catch (TaskCanceledException) when (!Ct.IsCancellationRequested) { await Say("Сервис не успел ответить. Повторите действие чуть позже."); }
    }

    private async Task SubmitExercise(ExerciseView exercise, object answer)
    {
        state.AnswerMessageId = context.Update.Id; state.AnswerExerciseId = exercise.Id;
        await db.SaveChangesAsync(Ct);
        using var response = await api.PostAsJsonAsync(Prefix + $"/exercises/{exercise.Id}/answer", new { answer }, Ct);
        if (response.StatusCode == HttpStatusCode.Conflict) { await Next(); return; }
        Check(response);
        var receipt = (await response.Content.ReadFromJsonAsync<Attempt>(Ct))!;
        await Say(receipt.Recognized ? "Ответ принят и распознан как верный." : "Ответ сохранён, но не совпал с ключом проверки.");
        await Next();
    }

    private async Task SubmitReview(ReviewDraft draft, ReviewAnswer answer)
    {
        using var response = await api.PostAsJsonAsync(Prefix + $"/reviews/{draft.Assignment.Id}/response", answer, Ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        { await Reset(); await Say("Состояние задания изменилось. Откройте проверку заново.", TaskButtons.Kinds()); return; }
        Check(response);
        await Reset();
        if (draft.Boss)
        {
            await Say(answer.Action == "Skip" ? "Босс ещё не пройден. Подтвердите или исправьте перевод." : "Босс пройден! Ваш перевод сохранён для согласования.");
            await Next();
            return;
        }
        await Say(answer.Action == "Skip" ? "Перевод пропущен. Выберите следующее задание." : "Ваш ответ сохранён. Выберите следующее задание.", TaskButtons.Kinds());
    }

    private bool MatchesTask(string[] args) => args.Length == 2 && Guid.TryParseExact(args[0], "N", out var id) && state.TaskId == id;
    private Task Stale() => Say("Эта кнопка относится к предыдущему заданию. Используйте кнопки текущего задания или /tasks.");
    private ExerciseDraft ReadExercise() => JsonSerializer.Deserialize<ExerciseDraft>(state.State[9..])!;
    private Task SaveExercise(ExerciseDraft draft) => Save("exercise:" + JsonSerializer.Serialize(draft), draft.Exercise.Id);
    private Task SaveReview(ReviewDraft draft) => Save("review:" + JsonSerializer.Serialize(draft), draft.Assignment.Id);
    private Task Reset() => Save("idle", null);
    private async Task Save(string value, Guid? id)
    { state.State = value; state.TaskId = id; state.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(Ct); }
    private async Task ShowMenu()
    {
        await Reset();
        await Say("Выберите «Профиль», «Задания на сегодня» или «Виды заданий». Для первого входа привяжите Telegram:\n" + Portal);
    }
    private Task ShowKinds() => Say("Выберите вид задания или продолжите дневной урок.", TaskButtons.Kinds());

    private async Task Next()
    {
        var lesson = await Get<Lesson>(Prefix + "/lesson");
        var next = lesson.Exercises.FirstOrDefault(x => !x.Completed);
        if (next is null)
        {
            if (!lesson.BossCompleted)
            {
                using var response = await api.PostAsJsonAsync(Prefix + "/lesson/boss", new { }, Ct);
                Check(response);
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    await Reset();
                    await Say("Упражнения выполнены. Для финального босса пока нет доступного перевода. Вернитесь к заданиям позже.");
                    return;
                }
                var review = (await response.Content.ReadFromJsonAsync<ReviewView>(Ct))!;
                await SaveReview(new(review, Boss: true));
                await Say("👑 Финальный босс дневного урока\nПодтвердите или исправьте перевод реального фрагмента.\n\n" + ReviewChat.Prompt(review), TaskButtons.Review(review, boss: true));
                return;
            }
            await Reset();
            await Say(lesson.Exercises.Length == 0 ? "В дневном уроке пока нет заданий. Выберите вид упражнения или проверку перевода." :
                $"Задания за {lesson.Date:dd.MM.yyyy} выполнены: {lesson.Exercises.Length} из {lesson.Exercises.Length}. Можно выбрать другой вид задания.", TaskButtons.Kinds());
            return;
        }
        await ShowExercise(next, $"Задания за {lesson.Date:dd.MM.yyyy}. Выполнено: {lesson.Exercises.Count(x => x.Completed)}/{lesson.Exercises.Length}\n\n");
    }

    private async Task ShowExercise(ExerciseView exercise, string progress = "")
    {
        var draft = new ExerciseDraft(exercise, []);
        await SaveExercise(draft);
        await Say(progress + ExerciseChat.Prompt(exercise) + TaskButtons.Progress(draft), TaskButtons.Exercise(draft));
        if (exercise.Kind is "audio-dictation" or "audio-translation" && exercise.Prompt.TryGetProperty("audioUrl", out var url) && url.GetString() is { Length: > 0 } link)
            await audio.SendAsync(context.BotClient, chatId, link, Ct);
    }

    private async Task Say(string value, ReplyMarkup? keyboard = null)
    {
        for (var start = 0; start < value.Length;)
        {
            var length = Math.Min(4000, value.Length - start);
            if (char.IsHighSurrogate(value[start + length - 1])) length--;
            await context.Sender.SendText(chatId, new SendText { Text = value.Substring(start, length), ReplyMarkup = keyboard ?? TaskButtons.Menu() }, Ct);
            start += length;
        }
    }
    private async Task<T> Get<T>(string url)
    { using var response = await api.GetAsync(url, Ct); Check(response); return (await response.Content.ReadFromJsonAsync<T>(Ct))!; }
    private void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) throw new StudentApiException("Привяжите Telegram на сайте:\n" + Portal);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new StudentApiException("Проверьте привязку и доступ к учётной записи ученика.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new StudentApiException("Ответ не принят или срок задания истёк. Откройте /tasks и выберите задание заново.");
        response.EnsureSuccessStatusCode();
    }
    private sealed class StudentApiException(string message) : Exception(message);
    private sealed record Profile(Guid Id, string Username, Guid? SchoolId, Guid? ClassId, string? SchoolName, string? ClassName);
    private sealed record Lesson(Guid Id, DateOnly Date, ExerciseView[] Exercises, bool BossCompleted = false);
    private sealed record Attempt(Guid Id, bool Recognized);
}

public sealed record ExerciseView(Guid Id, string Original, bool Completed, string Kind, int SchemaVersion, JsonElement Prompt);
