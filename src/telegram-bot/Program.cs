using System.Net.Http.Json;
using System.Text.Json;

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
long offset = 0;

while (true)
{
    try
    {
        using var response = await telegram.GetAsync($"getUpdates?timeout=25&offset={offset}&allowed_updates=%5B%22message%22%5D");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            offset = update.GetProperty("update_id").GetInt64() + 1;
            if (!update.TryGetProperty("message", out var message) || !message.TryGetProperty("text", out var textValue)) continue;
            var text = textValue.GetString()?.Trim() ?? "";
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
            var from = message.GetProperty("from");
            var userId = from.GetProperty("id").GetInt64();
            var name = from.TryGetProperty("first_name", out var first) ? first.GetString() ?? "Участник" : "Участник";
            if (message.GetProperty("chat").GetProperty("type").GetString() != "private") continue;
            var reply = await Handle(userId, text, name);
            await telegram.PostAsJsonAsync("sendMessage", new { chat_id = chatId, text = reply });
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
    var prefix = $"/api/internal/telegram/{id}";
    if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
    {
        using var result = await api.PostAsJsonAsync(prefix + "/start", new { name });
        return result.IsSuccessStatusCode ? "Һаумыһығыҙ! Выберите /task для задания дня, /score для баллов или /cancel для отмены." : "Не удалось зарегистрироваться. Попробуйте позже.";
    }
    if (text.StartsWith("/cancel", StringComparison.OrdinalIgnoreCase))
    {
        await api.PostAsJsonAsync(prefix + "/cancel", new { });
        return "Диалог отменён. Вернуться к заданию: /task";
    }
    if (text.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
    {
        using var result = await api.GetAsync(prefix + "/task");
        if (result.StatusCode == System.Net.HttpStatusCode.NotFound) return "Сначала отправьте /start.";
        if (!result.IsSuccessStatusCode) return "Задание пока недоступно.";
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var task = data.RootElement;
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
    using (var result = await api.PostAsJsonAsync(prefix + "/submit", new { action = text.Equals("да", StringComparison.OrdinalIgnoreCase) ? "confirm" : "correct", answerText = text }))
    {
        if (!result.IsSuccessStatusCode) return "Сначала отправьте /task, затем «да» или свой перевод.";
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var status = data.RootElement.GetProperty("status").GetString();
        return $"Ответ сохранён. +10 баллов. Статус перевода: {status switch { "confirmed" => "подтверждён", "disputed" => "есть разногласия", _ => "ожидает другие ответы" }}. Следующее задание появится завтра.";
    }
}
