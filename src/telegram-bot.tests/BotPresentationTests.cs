using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

public sealed class BotPresentationTests
{
    [Theory]
    [InlineData("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk")]
    [InlineData("987654321:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk")]
    public async Task StartupReplacesCommandsAndUploadsSchoolAvatarForEachToken(string token)
    {
        using var transport = new PresentationApi();
        using var http = new HttpClient(transport);
        var telegram = new TelegramBotClient(token, http);
        await BotPresentation.ApplyAsync(telegram, new ConfigurationBuilder().Build(), default);

        Assert.All(transport.Calls, call => Assert.Equal($"/bot{token}/{call.Method}", call.Path));
        var deletions = transport.Calls.Where(call => call.Method == "deleteMyCommands").ToArray();
        Assert.Equal(16, deletions.Length);
        foreach (var language in new[] { "", "ru", "ba", "en" })
        {
            Assert.Equal(new[] { "all_chat_administrators", "all_group_chats", "all_private_chats", "default" },
                deletions.Where(call => Language(call) == language).Select(call => Json(call).GetProperty("scope").GetProperty("type").GetString()).Order());
            var registration = Assert.Single(transport.Calls, call => call.Method == "setMyCommands" && Language(call) == language);
            Assert.Equal(new[] { "start", "profile", "level", "boss", "rating", "site", "tasks", "cancel", "help" },
                Json(registration).GetProperty("commands").EnumerateArray().Select(command => command.GetProperty("command").GetString()));
            var name = Assert.Single(transport.Calls, call => call.Method == "setMyName" && Language(call) == language);
            Assert.Equal("Башкирская языковая школа", Json(name).GetProperty("name").GetString());
            var description = Assert.Single(transport.Calls, call => call.Method == "setMyDescription" && Language(call) == language);
            Assert.Contains("/start", Json(description).GetProperty("description").GetString());
            var about = Assert.Single(transport.Calls, call => call.Method == "setMyShortDescription" && Language(call) == language);
            Assert.Contains("bashperevod.ru", Json(about).GetProperty("short_description").GetString());
        }
        Assert.All(transport.Calls.Take(16), call => Assert.Equal("deleteMyCommands", call.Method));
        var menu = Assert.Single(transport.Calls, call => call.Method == "setChatMenuButton");
        Assert.Equal("commands", Json(menu).GetProperty("menu_button").GetProperty("type").GetString());
        var photo = Assert.Single(transport.Calls, call => call.Method == "setMyProfilePhoto");
        Assert.Contains("multipart/form-data", photo.ContentType);
        Assert.Contains("school-logo.jpg", photo.Body);
        Assert.True(photo.Bytes.AsSpan().IndexOf(new byte[] { 0xff, 0xd8, 0xff }) >= 0, "Upload must contain a JPEG image.");
    }

    [Fact]
    public async Task ConfiguredPresentationReplacesDefaultsAndResetsAdditionalLanguage()
    {
        using var transport = new PresentationApi();
        using var http = new HttpClient(transport);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BotPresentation:Name"] = "Новая школа",
            ["BotPresentation:Description"] = "Описание школы",
            ["BotPresentation:ShortDescription"] = "О школе",
            ["BotPresentation:LanguageCodes:0"] = "de",
            ["BotPresentation:LanguageCodes:1"] = "de"
        }).Build();
        await BotPresentation.ApplyAsync(new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", http), configuration, default);
        Assert.Equal(8, transport.Calls.Count(call => call.Method == "deleteMyCommands"));
        Assert.All(transport.Calls.Where(call => call.Method == "setMyName"), call => Assert.Equal("Новая школа", Json(call).GetProperty("name").GetString()));
        Assert.All(transport.Calls.Where(call => call.Method == "setMyDescription"), call => Assert.Equal("Описание школы", Json(call).GetProperty("description").GetString()));
        Assert.All(transport.Calls.Where(call => call.Method == "setMyShortDescription"), call => Assert.Equal("О школе", Json(call).GetProperty("short_description").GetString()));
        Assert.Equal(new[] { "", "de" }, transport.Calls.Where(call => call.Method == "setMyCommands").Select(Language));
    }

    [Fact]
    public async Task FailedResetStopsPresentationBeforePublishingNewCommands()
    {
        using var transport = new PresentationApi { RejectReset = true };
        using var http = new HttpClient(transport);
        await Assert.ThrowsAsync<ApiRequestException>(() => BotPresentation.ApplyAsync(
            new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", http), new ConfigurationBuilder().Build(), default));
        Assert.Equal("deleteMyCommands", Assert.Single(transport.Calls).Method);
    }

    private static JsonElement Json(ApiCall call) => JsonDocument.Parse(call.Body).RootElement.Clone();
    private static string Language(ApiCall call) => Json(call).TryGetProperty("language_code", out var language) ? language.GetString() ?? "" : "";
    private sealed record ApiCall(string Method, string Path, string Body, byte[] Bytes, string? ContentType);
    private sealed class PresentationApi : HttpMessageHandler
    {
        public bool RejectReset { get; init; }
        public List<ApiCall> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = path.Split('/')[^1];
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Calls.Add(new ApiCall(method, path, Encoding.UTF8.GetString(bytes), bytes, request.Content.Headers.ContentType?.ToString()));
            if (RejectReset && method == "deleteMyCommands")
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { ok = false, error_code = 400, description = "Reset rejected" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result = true }) };
        }
    }
}
