using Telegram.Bot;
using Telegram.Bot.Types;

public sealed class ExerciseAudio(HttpClient http) : IDisposable
{
    public async Task SendAsync(ITelegramBotClient bot, long chatId, string url, CancellationToken ct)
    {
        // The storage client deliberately has no internal API credentials.
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is "audio/mpeg" or "audio/mp4")
            await bot.SendAudio(chatId, InputFile.FromStream(stream, type == "audio/mpeg" ? "exercise.mp3" : "exercise.m4a"), cancellationToken: ct);
        else
            // Silero produces WAV; Telegram accepts it as an uploaded file, not sendAudio.
            await bot.SendDocument(chatId, InputFile.FromStream(stream, "exercise.wav"), caption: "Прослушайте запись и отправьте ответ сообщением.", cancellationToken: ct);
    }

    public void Dispose() => http.Dispose();
}
