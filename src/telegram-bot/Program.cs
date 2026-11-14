using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

var factory = new BotDbContextFactory();
await using var cursorDb = factory.CreateDbContext([]);
await cursorDb.Database.MigrateAsync();
var cursor = await cursorDb.Cursors.FirstOrDefaultAsync(c => c.Id == 1);
if (cursor is null)
{
    cursor = new BotCursor();
    cursorDb.Cursors.Add(cursor);
    await cursorDb.SaveChangesAsync();
}

var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Telegram bot is disabled: set TELEGRAM_BOT_TOKEN in .env");
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}
var key = Environment.GetEnvironmentVariable("INTERNAL_API_KEY") ?? throw new InvalidOperationException("INTERNAL_API_KEY is required");
var mainUrl = Environment.GetEnvironmentVariable("MAIN_API_URL") ?? "http://main-api:8080";
using var telegram = new HttpClient { BaseAddress = new Uri($"https://api.telegram.org/bot{token}/"), Timeout = TimeSpan.FromSeconds(40) };
using var api = new HttpClient { BaseAddress = new Uri(mainUrl), Timeout = TimeSpan.FromSeconds(20) };
api.DefaultRequestHeaders.Add("X-Internal-Key", key);
long offset = cursor.NextOffset;

while (true)
{
    try
    {
        using var response = await telegram.GetAsync($"getUpdates?timeout=25&offset={offset}&allowed_updates=%5B%22message%22%5D");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var nextOffset = update.GetProperty("update_id").GetInt64() + 1;
            if (update.TryGetProperty("message", out var message) && message.TryGetProperty("text", out var textValue)
                && message.GetProperty("chat").GetProperty("type").GetString() == "private")
            {
                var text = textValue.GetString()?.Trim() ?? "";
                var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
                var from = message.GetProperty("from");
                var userId = from.GetProperty("id").GetInt64();
                var name = from.TryGetProperty("first_name", out var first) ? first.GetString() ?? "Участник" : "Участник";
                var reply = await Handle(userId, text, name);
                using var sent = await telegram.PostAsJsonAsync("sendMessage", new { chat_id = chatId, text = reply });
                sent.EnsureSuccessStatusCode();
            }
            offset = nextOffset;
            cursor.NextOffset = offset;
            await cursorDb.SaveChangesAsync();
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Bot polling error: {ex.GetType().Name}: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(5));
    }
}

async Task<string> Handle(long id, string text, string name)
{
    await using var db = factory.CreateDbContext([]);
    var state = await db.States.FirstOrDefaultAsync(s => s.TelegramId == id);
    var prefix = $"/api/internal/telegram/{id}";
    if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
    {
        using var result = await api.PostAsJsonAsync(prefix + "/start", new { name });
        if (!result.IsSuccessStatusCode) return "Не удалось зарегистрироваться. Попробуйте позже.";
        if (state is null) { state = new BotState { TelegramId = id }; db.States.Add(state); }
        state.State = "idle"; state.TaskId = null; state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return "Һаумыһығыҙ! Выберите /task для задания дня, /score для баллов или /cancel для отмены.";
    }
    if (text.StartsWith("/cancel", StringComparison.OrdinalIgnoreCase))
    {
        if (state is not null)
        {
            state.State = "idle"; state.TaskId = null; state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return "Диалог отменён. Вернуться к заданию: /task";
    }
    if (text.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
    {
        using var result = await api.GetAsync(prefix + "/task");
        if (result.StatusCode == System.Net.HttpStatusCode.NotFound) return "Сначала отправьте /start.";
        if (!result.IsSuccessStatusCode) return "Задание пока недоступно.";
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var task = data.RootElement;
        if (state is null) { state = new BotState { TelegramId = id }; db.States.Add(state); }
        state.State = "awaiting_answer"; state.TaskId = task.GetProperty("id").GetGuid(); state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var submitted = task.TryGetProperty("submitted_text", out var answer) && answer.ValueKind == JsonValueKind.String ? $"\nВаш ответ: {answer.GetString()}" : "";
        return $"Переведите с башкирского на русский:\n\n{task.GetProperty("text_ba").GetString()}\n\nПредложенный перевод: {task.GetProperty("candidate").GetString()}\nИсточник: {task.GetProperty("source_title").GetString()}{submitted}\n\nОтветьте «да», чтобы подтвердить, или отправьте свой перевод. /cancel — отмена.";
    }
    if (text.StartsWith("/score", StringComparison.OrdinalIgnoreCase))
    {
        using var result = await api.GetAsync(prefix + "/me");
        if (!result.IsSuccessStatusCode) return "Сначала отправьте /start.";
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        return $"{data.RootElement.GetProperty("display_name").GetString()}, у вас {data.RootElement.GetProperty("points").GetInt32()} баллов.";
    }
    if (text.StartsWith('/')) return "Команды: /start, /task, /score, /cancel.";
    if (state?.State != "awaiting_answer" || state.TaskId is not Guid taskId)
        return "Сначала отправьте /task, затем «да» или свой перевод.";
    using (var result = await api.PostAsJsonAsync(prefix + "/submit", new {
        taskId, action = text.Equals("да", StringComparison.OrdinalIgnoreCase) ? "confirm" : "correct", answerText = text }))
    {
        if (!result.IsSuccessStatusCode) return "Не удалось сохранить ответ. Попробуйте /task ещё раз.";
        state.State = "idle"; state.TaskId = null; state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var status = data.RootElement.GetProperty("status").GetString();
        return $"Ответ сохранён. +10 баллов. Статус перевода: {status switch { "confirmed" => "подтверждён", "disputed" => "есть разногласия", _ => "ожидает другие ответы" }}. Следующее задание появится завтра.";
    }
}
