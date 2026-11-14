using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotKit;
using TelegramBotKit.Fallbacks;
using TelegramBotKit.Messaging;

public sealed class StudentConversation(HttpClient api, IDbContextFactory<BotDbContext> factory, ExerciseAudio audioSender) : IDefaultMessageHandler
{
    private static readonly ReplyKeyboardMarkup Menu = new(new[] {
        new KeyboardButton[] { new("Профиль"), new("Задания на сегодня") }
    }) { ResizeKeyboard = true };
    private static readonly string Portal = Environment.GetEnvironmentVariable("STUDENT_PORTAL_URL") ?? "https://bashperevod.ru/telegram.html";

    public async Task HandleAsync(Message message, BotContext ctx)
    {
        if (message.Chat.Type != ChatType.Private || message.From is not { IsBot: false } user) return;
        var ct = ctx.CancellationToken;
        var text = message.Text?.Trim() ?? "";
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts.FirstOrDefault()?.Split('@')[0].ToLowerInvariant() ?? "";
        var prefix = $"/internal/telegram/{user.Id}";
        await using var db = await factory.CreateDbContextAsync(ct);
        var state = await db.States.SingleOrDefaultAsync(x => x.TelegramId == user.Id, ct);
        if (state is null) { state = new() { TelegramId = user.Id }; db.States.Add(state); }

        try
        {
            if (command == "/start" && parts.Length == 2)
            {
                using var link = await api.PostAsJsonAsync(prefix + "/link", new { code = parts[1] }, ct);
                if (!link.IsSuccessStatusCode) { await Say("Ссылка недействительна, истекла или учётная запись уже привязана. Получите новую ссылку:\n" + Portal); return; }
                await Reset();
                await Say("Telegram привязан. Здесь доступны ваш профиль и ежедневные задания.");
                return;
            }
            if (command is "/start" or "/help" or "/cancel")
            {
                await Reset();
                await Say("Выберите «Профиль» или «Задания на сегодня». Для первого входа привяжите Telegram:\n" + Portal);
                return;
            }
            if (command is "/profile" or "/score" || text == "Профиль")
            {
                var profile = await Get<Profile>(prefix + "/profile");
                await Reset();
                await Say($"Ваш профиль\nЛогин: {profile.Username}\nШкола: {profile.SchoolName ?? "не указана"}\nКласс: {profile.ClassName ?? "не указан"}");
                return;
            }
            if (text == "Виды заданий")
            {
                await Say("Выберите вид задания. Можно продолжить дневной урок или отдельно потренироваться.", ExerciseChat.KindsKeyboard());
                return;
            }
            if (ExerciseChat.Kinds.TryGetValue(text, out var kind))
            {
                using var issued = await api.PostAsJsonAsync(prefix + "/lesson/exercises", new { kind, languagePair = "ba-ru" }, ct);
                await Check(issued);
                if (issued.StatusCode == HttpStatusCode.NoContent) { await Say("Для этого вида пока нет подходящих материалов. Выберите другое задание.", ExerciseChat.KindsKeyboard()); return; }
                var selected = (await issued.Content.ReadFromJsonAsync<ExerciseView>(ct))!;
                if (selected.Completed) { await Say("Это упражнение сегодня уже выполнено. Выберите другой вид или продолжите урок.", ExerciseChat.KindsKeyboard()); return; }
                await ShowExercise(selected);
                return;
            }
            if (text is "Выбор перевода" or "Подтверждение перевода" or "Предложить перевод")
            {
                var reviewKind = text switch { "Выбор перевода" => "translation-choice", "Предложить перевод" => "translation-create", _ => "translation-confirm" };
                using var issued = await api.PostAsJsonAsync(prefix + "/reviews/next?kind=" + reviewKind, new { }, ct);
                await Check(issued);
                if (issued.StatusCode == HttpStatusCode.NoContent) { await Say("Сейчас нет доступных переводов для проверки. Можно пройти учебные упражнения.", ExerciseChat.KindsKeyboard()); return; }
                var review = (await issued.Content.ReadFromJsonAsync<ReviewView>(ct))!;
                state.State = "review:" + JsonSerializer.Serialize(new ReviewDraft(review)); state.TaskId = review.Id; state.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await Say(ReviewChat.Prompt(review), ReviewChat.Keyboard(review));
                return;
            }
            if (command is "/daily" or "/task" || text is "Задания на сегодня" or "Продолжить урок") { await Next(); return; }
            if (text.StartsWith('/')) { await Say("Доступны /profile, /daily и /cancel."); return; }
            if (state.State.StartsWith("review:", StringComparison.Ordinal))
            {
                var draft = JsonSerializer.Deserialize<ReviewDraft>(state.State[7..])!;
                if (text == "Свой перевод" && draft.Assignment.AllowCustom)
                {
                    state.State = "review:" + JsonSerializer.Serialize(draft with { Custom = true });
                    await db.SaveChangesAsync(ct);
                    await Say("Напишите свой перевод одним сообщением. /cancel — вернуться в меню.");
                    return;
                }
                ReviewAnswer reviewAnswer;
                try { reviewAnswer = ReviewChat.Answer(draft.Assignment, text, draft.Custom); }
                catch (FormatException) { await Say("Выберите номер из показанных вариантов или напишите свой перевод.", ReviewChat.Keyboard(draft.Assignment)); return; }
                using var result = await api.PostAsJsonAsync(prefix + $"/reviews/{draft.Assignment.Id}/response", reviewAnswer, ct);
                if (result.StatusCode == HttpStatusCode.Conflict)
                {
                    await Reset(); await Say("Состояние задания изменилось. Откройте проверку перевода заново.", ExerciseChat.KindsKeyboard()); return;
                }
                await Check(result);
                await Reset();
                await Say(reviewAnswer.Action == "Skip" ? "Перевод пропущен. Выберите следующее задание." : "Ваш ответ сохранён. Выберите следующее задание.", ExerciseChat.KindsKeyboard());
                return;
            }
            // Redelivery after a crash must keep the original exercise, even if Next already ran.
            var target = state.AnswerMessageId == message.Id ? state.AnswerExerciseId : state.TaskId;
            if (target is not Guid exerciseId || state.State != "awaiting_answer") { await Say("Нажмите «Задания на сегодня», чтобы начать."); return; }
            var lesson = await Get<Lesson>(prefix + "/lesson");
            var exercise = lesson.Exercises.SingleOrDefault(x => x.Id == exerciseId && !x.Completed);
            if (exercise is null) { await Next(); return; }
            if (text.Length == 0) { await Say("Отправьте ответ текстом. /cancel — вернуться в меню."); return; }
            object answer;
            try { answer = ExerciseChat.Answer(exercise, text); }
            catch (FormatException) { await Say("Проверьте формат ответа.\n" + ExerciseChat.Prompt(exercise), ExerciseChat.Keyboard(exercise)); return; }
            state.AnswerMessageId = message.Id; state.AnswerExerciseId = exerciseId;
            await db.SaveChangesAsync(ct);
            using var response = await api.PostAsJsonAsync(prefix + $"/exercises/{exerciseId}/answer", new { answer }, ct);
            if (response.StatusCode == HttpStatusCode.Conflict) { await Say("Ответ уже был сохранён. Продолжим задания."); await Next(); return; }
            await Check(response);
            var receipt = (await response.Content.ReadFromJsonAsync<Attempt>(ct))!;
            await Say(receipt.Recognized ? "Ответ принят и распознан как верный." : "Ответ сохранён, но не совпал с ключом проверки.");
            await Next();
        }
        catch (StudentApiException ex) { await Reset(); await Say(ex.Message); }
        catch (HttpRequestException) { await Say("Сервис временно недоступен. Повторите действие чуть позже."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { await Say("Сервис не успел ответить. Повторите действие чуть позже."); }

        async Task Say(string value, ReplyKeyboardMarkup? keyboard = null)
        {
            for (var start = 0; start < value.Length;)
            {
                var length = Math.Min(4000, value.Length - start);
                if (char.IsHighSurrogate(value[start + length - 1])) length--;
                await ctx.Sender.SendText(message.Chat.Id, new SendText { Text = value.Substring(start, length), ReplyMarkup = keyboard ?? Menu }, ct);
                start += length;
            }
        }
        async Task Reset() { state.State = "idle"; state.TaskId = null; state.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        async Task<T> Get<T>(string url)
        {
            using var response = await api.GetAsync(url, ct);
            await Check(response);
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        }
        Task Check(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) throw new StudentApiException("Привяжите Telegram на сайте, затем откройте полученную ссылку:\n" + Portal);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new StudentApiException("Доступ недоступен. Проверьте привязку и доступ к учётной записи ученика.");
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new StudentApiException("Не удалось принять ответ. Откройте /daily и проверьте формат.");
            response.EnsureSuccessStatusCode();
            return Task.CompletedTask;
        }
        async Task Next()
        {
            var lesson = await Get<Lesson>(prefix + "/lesson");
            var next = lesson.Exercises.FirstOrDefault(x => !x.Completed);
            if (next is null)
            {
                await Reset();
                await Say(lesson.Exercises.Length == 0 ? "В дневном уроке пока нет заданий. Выберите вид упражнения или проверку перевода." : $"Задания за {lesson.Date:dd.MM.yyyy} выполнены: {lesson.Exercises.Length} из {lesson.Exercises.Length}. Можно выбрать другой вид задания.", ExerciseChat.KindsKeyboard());
                return;
            }
            await ShowExercise(next, $"Задания за {lesson.Date:dd.MM.yyyy}. Выполнено: {lesson.Exercises.Count(x => x.Completed)}/{lesson.Exercises.Length}\n\n");
        }
        async Task ShowExercise(ExerciseView next, string progress = "")
        {
            state.State = "awaiting_answer"; state.TaskId = next.Id; state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await Say(progress + ExerciseChat.Prompt(next), ExerciseChat.Keyboard(next));
            if (next.Kind is "audio-dictation" or "audio-translation" && next.Prompt.TryGetProperty("audioUrl", out var audio) && audio.GetString() is { Length: > 0 } url)
                await audioSender.SendAsync(ctx.BotClient, message.Chat.Id, url, ct);
        }
    }

    private sealed class StudentApiException(string message) : Exception(message);
    private sealed record Profile(Guid Id, string Username, Guid? SchoolId, Guid? ClassId, string? SchoolName, string? ClassName);
    private sealed record Lesson(Guid Id, DateOnly Date, ExerciseView[] Exercises);
    private sealed record Attempt(Guid Id, bool Recognized);
}

public sealed record ExerciseView(Guid Id, string Original, bool Completed, string Kind, int SchemaVersion, JsonElement Prompt);
