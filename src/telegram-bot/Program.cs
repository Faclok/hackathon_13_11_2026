using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
using var diagnosticLogs = LoggerFactory.Create(logging => logging
    .AddConfiguration(builder.Configuration.GetSection("Logging"))
    .AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss "));
var logger = diagnosticLogs.CreateLogger("BotStartup");
logger.LogInformation("Configuration loaded from {ContentRoot}; environment: {Environment}", builder.Environment.ContentRootPath, builder.Environment.EnvironmentName);
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
    builder.Configuration.GetConnectionString("Default") ?? "Host=localhost;Database=bot;Username=postgres;Password=postgres",
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Bot")));
using var telegramHandler = new HttpClientHandler();
var proxySettings = builder.Configuration.GetSection("TelegramProxy");
if (proxySettings.GetValue<bool>("Enabled"))
{
    if (!Uri.TryCreate(proxySettings["Address"], UriKind.Absolute, out var proxyAddress) ||
        proxyAddress.Scheme is not ("http" or "https" or "socks4" or "socks4a" or "socks5"))
        throw new InvalidOperationException("TelegramProxy:Address must be an absolute HTTP, HTTPS or SOCKS proxy URL.");

    var proxy = new WebProxy(proxyAddress);
    if (!string.IsNullOrWhiteSpace(proxySettings["Username"]))
        proxy.Credentials = new NetworkCredential(proxySettings["Username"], proxySettings["Password"]);
    telegramHandler.Proxy = proxy;
    telegramHandler.UseProxy = true;
}
logger.LogInformation("Telegram proxy enabled: {Enabled}; explicit proxy configured: {Configured}",
    proxySettings.GetValue<bool>("Enabled"), telegramHandler.Proxy is not null);
logger.LogInformation("Bot database connection configured: {Configured}; main API: {ApiHost}",
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")), api.BaseAddress?.Host);
using var telegramTransport = new TelegramHttpDiagnostics(diagnosticLogs.CreateLogger<TelegramHttpDiagnostics>()) { InnerHandler = telegramHandler };
using var telegramHttp = new HttpClient(telegramTransport, disposeHandler: false);
var bot = builder.Services.AddTelegramBotKit(options =>
{
    options.Polling.Limit = 100;
    options.Polling.TimeoutSeconds = 25;
    builder.Configuration.GetSection("TelegramBotKit").Bind(options);
    options.Token = token;
    // Private messages and callback queries share persistent student state.
    options.Polling.MaxDegreeOfParallelism = 1;
    options.Polling.AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery];
}, telegramHttp);
bot.UseMiddleware<UpdateDiagnostics>();
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
var stage = "database migrations";
try
{
    logger.LogInformation("Starting database migrations; Telegram polling has not started yet");
    await using (var db = host.Services.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContext())
        await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations completed");
    var telegram = host.Services.GetRequiredService<ITelegramBotClient>();
    stage = "Telegram identity and webhook check";
    using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var identity = await telegram.GetMe(startupTimeout.Token);
    logger.LogInformation("Telegram connection verified; actual bot: @{Username}, id: {BotId}", identity.Username, identity.Id);
    var webhook = await telegram.GetWebhookInfo(startupTimeout.Token);
    logger.LogInformation("Telegram webhook configured: {Configured}; pending updates: {Pending}", !string.IsNullOrEmpty(webhook.Url), webhook.PendingUpdateCount);
    if (!string.IsNullOrEmpty(webhook.Url))
        logger.LogWarning("An active webhook conflicts with polling. Remove the webhook before using this polling bot");
    stage = "Telegram command registration";
    logger.LogInformation("Registering Telegram commands");
    await telegram.SetMyCommands([
        new() { Command = "start", Description = "Привязка и главное меню" },
        new() { Command = "profile", Description = "Мой профиль" },
        new() { Command = "daily", Description = "Продолжить дневной урок" },
        new() { Command = "tasks", Description = "Выбрать вид задания" },
        new() { Command = "cancel", Description = "Вернуться в меню" }
    ], cancellationToken: startupTimeout.Token);
    logger.LogInformation("Commands registered; starting host and Telegram polling");
    host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() =>
        logger.LogInformation("Host started; send /start in a private chat with @{Username}", identity.Username));
    stage = "host / Telegram polling";
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical("Bot failed during {Stage}: {Failure}", stage, BotDiagnosticErrors.Describe(ex));
    throw;
}
