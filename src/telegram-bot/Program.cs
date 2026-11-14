using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Dispatching;
using TelegramBotKit.Fallbacks;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
var ct = shutdown.Token;
var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Telegram bot is disabled: set TELEGRAM_BOT_TOKEN.");
    try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
    return;
}
var key = Environment.GetEnvironmentVariable("INTERNAL_API_KEY");
if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("INTERNAL_API_KEY is required.");
using var api = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("MAIN_API_URL") ?? "http://main-api:8080"), Timeout = TimeSpan.FromSeconds(30) };
api.DefaultRequestHeaders.Add("X-Internal-Key", key);
var services = new ServiceCollection();
services.AddLogging();
services.AddTelegramBotKit(options => options.Token = token);
services.AddSingleton(api);
services.AddSingleton(_ => new ExerciseAudio(new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 20_000_000 }));
services.AddSingleton<BotDbContextFactory>();
services.AddSingleton<IDbContextFactory<BotDbContext>>(sp => sp.GetRequiredService<BotDbContextFactory>());
services.AddScoped<IDefaultMessageHandler, StudentConversation>();
await using var provider = services.BuildServiceProvider();
var telegram = provider.GetRequiredService<ITelegramBotClient>();
var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
var factory = provider.GetRequiredService<BotDbContextFactory>();
await using var db = factory.CreateDbContext([]);
await db.Database.MigrateAsync(ct);
var cursor = await db.Cursors.SingleOrDefaultAsync(x => x.Id == 1, ct);
if (cursor is null) { cursor = new(); db.Cursors.Add(cursor); await db.SaveChangesAsync(ct); }
await telegram.SetMyCommands([
    new() { Command = "profile", Description = "Мой профиль" },
    new() { Command = "daily", Description = "Задания на сегодня" },
    new() { Command = "cancel", Description = "Вернуться в меню" }
], cancellationToken: ct);
while (!ct.IsCancellationRequested)
{
    try
    {
        var updates = await telegram.GetUpdates(offset: checked((int)cursor.NextOffset), timeout: 25,
            allowedUpdates: [UpdateType.Message], cancellationToken: ct);
        foreach (var update in updates)
        {
            // Sequential dispatch preserves each student's conversation and the durable cursor.
            await dispatcher.DispatchAsync(update, ct);
            cursor.NextOffset = (long)update.Id + 1;
            await db.SaveChangesAsync(ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
    catch (Exception ex)
    {
        // Exception messages from HTTP clients can contain the bot token.
        Console.Error.WriteLine($"Bot update failed: {ex.GetType().Name}");
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
    }
}
