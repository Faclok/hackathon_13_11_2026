using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var token = builder.Configuration["TELEGRAM_BOT_TOKEN"] ?? builder.Configuration["TelegramBotKit:Token"];
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Telegram bot is disabled: set TELEGRAM_BOT_TOKEN.");
    await builder.Build().RunAsync();
    return;
}
var key = builder.Configuration["INTERNAL_API_KEY"];
if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("INTERNAL_API_KEY is required.");
var api = new HttpClient { BaseAddress = new Uri(builder.Configuration["MAIN_API_URL"] ?? "http://main-api:8080"), Timeout = TimeSpan.FromSeconds(30) };
api.DefaultRequestHeaders.Add("X-Internal-Key", key);
builder.Services.AddSingleton(api);
builder.Services.AddSingleton(_ => new ExerciseAudio(new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 20_000_000 }));
builder.Services.AddDbContextFactory<BotDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Core") ?? "Host=localhost;Database=bot;Username=postgres;Password=postgres",
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Bot")));
var bot = builder.Services.AddTelegramBotKit(options =>
{
    options.Polling.Limit = 100;
    options.Polling.TimeoutSeconds = 25;
    builder.Configuration.GetSection("TelegramBotKit").Bind(options);
    options.Token = token;
    // Private messages and callback queries share persistent student state.
    options.Polling.MaxDegreeOfParallelism = 1;
    options.Polling.AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery];
});
bot.UseMiddleware<PrivateStudentChat>();
bot.UseQueuedMessageSender(options =>
{
    options.GlobalMaxPerSecond = 25;
    options.PerChatMinDelay = TimeSpan.FromSeconds(1);
    options.MaxRetryAttempts = 5;
});
builder.Services.AddStudentCommands();
builder.Services.AddTelegramBotKitPolling();
using var host = builder.Build();
await using (var db = host.Services.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContext())
    await db.Database.MigrateAsync();
await host.Services.GetRequiredService<ITelegramBotClient>().SetMyCommands([
    new() { Command = "start", Description = "Привязка и главное меню" },
    new() { Command = "profile", Description = "Мой профиль" },
    new() { Command = "daily", Description = "Продолжить дневной урок" },
    new() { Command = "tasks", Description = "Выбрать вид задания" },
    new() { Command = "cancel", Description = "Вернуться в меню" }
]);
await host.RunAsync();
