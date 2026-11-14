using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Dispatching;
using TelegramBotKit.Fallbacks;

public sealed class StudentConversationTests
{
    [Fact]
    public async Task CurrentLevelSurvivesRestartAndRedeliveryNeverAnswersNextQuestion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi(); var telegram = new TelegramApi();
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, "/level"), default);
            await dispatcher.DispatchAsync(Update(2, "Һаумы"), default);
            Assert.Contains(telegram.Messages, x => x.Contains("Верно!"));
        }
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(3, "/level"), default);
            await dispatcher.DispatchAsync(Update(2, "Һаумы"), default);
            Assert.Single(api.Answers);
            await dispatcher.DispatchAsync(Update(4, "Икенсе"), default);
        }
        Assert.Equal(2, api.Answers.Count);
        Assert.NotEqual(api.Answers[0].Question, api.Answers[1].Question);
        Assert.Contains(telegram.Messages, x => x.Contains("Уровень 7") && x.Contains("1/2"));
        Assert.Contains(telegram.Messages, x => x.Contains("Уровень пройден"));
        Assert.DoesNotContain(api.Paths, x => x.Contains("/lesson") || x.Contains("/exercises/"));
    }

    [Fact]
    public async Task LostResponseRetriesIdenticalTokenAndAnswerAfterRestart()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi { LoseResponse = true }; var telegram = new TelegramApi();
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, "/level"), default);
            await dispatcher.DispatchAsync(Update(2, "Первоначальный ответ"), default);
        }
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(3, "Другой текст после обрыва"), default);
            await dispatcher.DispatchAsync(Update(4, "/level"), default);
            await dispatcher.DispatchAsync(Update(3, "Другой текст после обрыва"), default);
        }
        Assert.Single(api.Answers);
        Assert.Equal(2, api.SubmissionBodies.Count);
        Assert.Equal(api.SubmissionBodies[0], api.SubmissionBodies[1]);
        using var retried = JsonDocument.Parse(api.SubmissionBodies[1]);
        Assert.Equal("Первоначальный ответ", retried.RootElement.GetProperty("answer").GetProperty("text").GetString());
        await using var state = factory.CreateDbContext();
        Assert.StartsWith("exercise:", (await state.States.SingleAsync()).State);
    }

    [Fact]
    public async Task ButtonsFromWrongAttemptCannotAnswerSameQuestionWithNewToken()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi { Kind = "missing-word", Wrong = true }; var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        await dispatcher.DispatchAsync(Update(1, "/level"), default);
        await dispatcher.DispatchAsync(Callback(2, $"answer {api.Token:N} g1"), default);
        await dispatcher.DispatchAsync(Update(3, "/level"), default);
        await dispatcher.DispatchAsync(Callback(4, $"answer {api.Token:N} g1"), default);
        Assert.Single(api.Answers);
        await dispatcher.DispatchAsync(Callback(5, $"answer {api.SecondToken:N} g0"), default);
        Assert.Equal(2, api.Answers.Count);
        Assert.Equal(api.Answers[0].Question, api.Answers[1].Question);
        Assert.NotEqual(api.Answers[0].Token, api.Answers[1].Token);
    }

    [Theory]
    [InlineData("translation-text", "Һаумы", "{\"text\":\"Һаумы\"}")]
    [InlineData("audio-dictation", "Һаумы", "{\"text\":\"Һаумы\"}")]
    [InlineData("audio-translation", "Привет", "{\"text\":\"Привет\"}")]
    [InlineData("missing-word", "2", "{\"optionId\":\"b\"}")]
    [InlineData("sentence-order", "2 1", "{\"tokenIds\":[\"b\",\"a\"]}")]
    [InlineData("word-match", "1-2 2-1", "{\"pairs\":[{\"leftId\":\"a\",\"rightId\":\"y\"},{\"leftId\":\"b\",\"rightId\":\"x\"}]}")]
    public async Task AllSixLevelKindsUseServerQuestionAndAttemptToken(string kind, string text, string expected)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi { Kind = kind, Count = 1 }; var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        await dispatcher.DispatchAsync(Update(1, "/level"), default);
        await dispatcher.DispatchAsync(Update(2, text), default);
        var answer = Assert.Single(api.Answers);
        Assert.Equal(api.Token, answer.Token);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, answer.Answer));
        Assert.Contains(telegram.Messages, x => x.Contains("Уровень пройден"));
        if (kind.StartsWith("audio-")) Assert.Equal(new[] { "sendDocument" }, telegram.Uploads);
    }

    [Theory]
    [InlineData("sentence-order", "{\"tokenIds\":[\"b\",\"a\"]}")]
    [InlineData("word-match", "{\"pairs\":[{\"leftId\":\"a\",\"rightId\":\"y\"},{\"leftId\":\"b\",\"rightId\":\"x\"}]}")]
    [InlineData("missing-word", "{\"optionId\":\"b\"}")]
    public async Task NativeButtonsBuildAnswersAndRejectStaleCallbacks(string kind, string expected)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi { Kind = kind }; var telegram = new TelegramApi();
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, "/level"), default);
            var first = kind == "sentence-order" ? "t1" : kind == "word-match" ? "l0" : "g1";
            await dispatcher.DispatchAsync(Callback(2, $"answer {api.Token:N} {first}"), default);
            await dispatcher.DispatchAsync(Callback(2, $"answer {api.Token:N} {first}"), default);
        }
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            var rest = kind == "sentence-order" ? new[] { "t0", "send" } : kind == "word-match" ? new[] { "r1", "l1", "r0", "send" } : [];
            for (var i = 0; i < rest.Length; i++) await dispatcher.DispatchAsync(Callback(3 + i, $"answer {api.Token:N} {rest[i]}"), default);
            await dispatcher.DispatchAsync(Update(20, "/level"), default);
            await dispatcher.DispatchAsync(Callback(21, $"answer {api.Token:N} send"), default);
        }
        var answer = Assert.Single(api.Answers);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, answer.Answer));
        Assert.Contains(telegram.Messages, x => x.Contains("предыдущему заданию"));
    }

    [Fact]
    public async Task BossHasThreeRealReviewsAndResumesBetweenSteps()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi(); var telegram = new TelegramApi();
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, "/boss"), default);
            await dispatcher.DispatchAsync(Update(2, "Мой перевод"), default);
        }
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(3, "/boss"), default);
            await dispatcher.DispatchAsync(Callback(4, $"vote {api.Assignments[1]:N} confirm"), default);
            await dispatcher.DispatchAsync(Update(5, "/boss"), default);
            await dispatcher.DispatchAsync(Callback(6, $"vote {api.Assignments[2]:N} v1"), default);
            await dispatcher.DispatchAsync(Callback(6, $"vote {api.Assignments[2]:N} v1"), default);
        }
        Assert.Equal(3, api.BossAnswers.Count);
        Assert.Equal("Мой перевод", api.BossAnswers[0].GetProperty("correction").GetString());
        Assert.Equal("Confirm", api.BossAnswers[1].GetProperty("action").GetString());
        Assert.Equal(api.Variant, api.BossAnswers[2].GetProperty("selectedVariantId").GetGuid());
        Assert.Contains(telegram.Messages, x => x.Contains("Босс пройден"));
        Assert.DoesNotContain(telegram.Messages, x => x.Contains("верный"));
    }

    [Fact]
    public async Task IncorrectAndEmptyStatesDoNotInventProgress()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi { Wrong = true }; var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        await dispatcher.DispatchAsync(Update(1, "/level"), default);
        await dispatcher.DispatchAsync(Update(2, "Ошибочный ответ"), default);
        Assert.Contains(telegram.Messages, x => x.Contains("вернётся в конце"));
        api.EmptyLevel = true; await dispatcher.DispatchAsync(Update(3, "/level"), default);
        Assert.Contains(telegram.Messages, x => x.Contains("пока нет материалов"));
        api.NoBoss = true; await dispatcher.DispatchAsync(Update(4, "/boss"), default);
        Assert.Contains(telegram.Messages, x => x.Contains("каждые пять"));
        api.NoBoss = false; api.EmptyBoss = true; await dispatcher.DispatchAsync(Update(5, "/boss"), default);
        Assert.Contains(telegram.Messages, x => x.Contains("сохранено 0/3"));
        Assert.Empty(api.BossAnswers);
    }

    [Fact]
    public async Task MenuUsesMiniAppButtonsForRatingAndFullSiteAndGroupsAreIgnored()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LevelApi(); var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        await dispatcher.DispatchAsync(Update(1, "/level", ChatType.Group), default);
        Assert.Empty(api.Paths); Assert.Empty(telegram.Messages);
        await dispatcher.DispatchAsync(Update(2, "/start"), default);
        Assert.Contains("https://bashperevod.ru/leaderboard?miniapp=1", telegram.WebApps);
        Assert.Contains("https://bashperevod.ru/?miniapp=1", telegram.WebApps);
        Assert.Empty(telegram.ReplyWebApps);
        Assert.Contains("Текущий уровень", telegram.Buttons); Assert.Contains("Босс", telegram.Buttons);
        await dispatcher.DispatchAsync(Update(3, "/profile"), default);
        Assert.Contains(telegram.Messages, x => x.Contains("Пройдено уровней: 6") && x.Contains("Доступно боссов: 1"));
    }

    private sealed class LevelApi : HttpMessageHandler
    {
        public string Kind { get; init; } = "translation-text";
        public int Count { get; init; } = 2;
        public bool LoseResponse { get; set; }
        public bool Wrong { get; init; }
        public bool EmptyLevel { get; set; }
        public bool NoBoss { get; set; }
        public bool EmptyBoss { get; set; }
        public Guid First { get; } = Guid.NewGuid();
        private readonly Guid second = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public Guid SecondToken { get; } = Guid.NewGuid();
        private readonly Guid level = Guid.NewGuid();
        private readonly Guid boss = Guid.NewGuid();
        public Guid Variant { get; } = Guid.NewGuid();
        public Guid[] Assignments { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public List<string> Paths { get; } = [];
        public List<string> SubmissionBodies { get; } = [];
        public List<(Guid Question, Guid Token, JsonElement Answer)> Answers { get; } = [];
        public List<JsonElement> BossAnswers { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            if (path.EndsWith("/profile")) return Ok(new { id = Guid.NewGuid(), username = "student" });
            if (path.EndsWith("/learning/progress")) return Ok(new LearningProgress(6, 0, 1, 15, 2));
            if (path.EndsWith("/learning/current"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                if (EmptyLevel) return new(HttpStatusCode.NoContent);
                var first = Answers.Count == 0 || Wrong;
                var prompt = JsonDocument.Parse(Kind switch
                {
                    "word-match" => """{"left":[{"id":"a","text":"бер"},{"id":"b","text":"ике"}],"right":[{"id":"x","text":"два"},{"id":"y","text":"один"}]}""",
                    "missing-word" => """{"before":"Һаумы, ","after":"!","options":[{"id":"a","text":"донъя"},{"id":"b","text":"мәктәп"}],"allowCustom":true}""",
                    "sentence-order" => """{"tokens":[{"id":"a","text":"донъя!"},{"id":"b","text":"Һаумы,"}]}""",
                    "audio-dictation" or "audio-translation" => """{"audioUrl":"https://storage.test/audio.wav?signature=test"}""",
                    _ => """{"text":"Һаумы!"}"""
                }).RootElement.Clone();
                return Ok(new LearningLevel(level, 7, Count, Wrong ? 0 : Answers.Count, new(first ? First : second, Answers.Count == 0 ? Token : SecondToken, Kind, 1, prompt)));
            }
            if (path.Contains("/learning/levels/") && path.EndsWith("/answer"))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                SubmissionBodies.Add(body.GetRawText());
                var token = body.GetProperty("attemptToken").GetGuid();
                var question = token == Token || Wrong ? First : second;
                Assert.Equal($"/internal/telegram/101/learning/levels/{level}/questions/{question}/answer", path);
                if (Answers.All(x => x.Token != token)) Answers.Add((question, token, body.GetProperty("answer").Clone()));
                if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("response lost after commit"); }
                return Ok(new LearningAnswer(token, !Wrong, !Wrong && Answers.Count == Count));
            }
            if (path.EndsWith("/learning/boss"))
            {
                if (NoBoss) return new(HttpStatusCode.NoContent);
                var index = BossAnswers.Count;
                var review = EmptyBoss ? null : new ReviewView(Assignments[index], Guid.NewGuid(), Variant, "Һаумы!", "Привет!", DateTimeOffset.UtcNow.AddHours(1),
                    index == 0 ? "translation-create" : index == 1 ? "translation-confirm" : "translation-choice", [new(Guid.NewGuid(), "Здравствуйте"), new(Variant, "Привет")], true);
                return Ok(new LearningBoss(boss, 5, 3, index, review));
            }
            if (path.Contains("/learning/bosses/") && path.EndsWith("/answer"))
            {
                Assert.Equal($"/internal/telegram/101/learning/bosses/{boss}/questions/{Assignments[BossAnswers.Count]}/answer", path);
                BossAnswers.Add((await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone());
                return Ok(new BossAnswer(Guid.NewGuid(), false, BossAnswers.Count == 3));
            }
            throw new InvalidOperationException("Unexpected route: " + path);
        }
        private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
    private sealed class TelegramApi : HttpMessageHandler
    {
        public List<string> Messages { get; } = [];
        public List<string> Buttons { get; } = [];
        public List<string> WebApps { get; } = [];
        public List<string> ReplyWebApps { get; } = [];
        public List<string> Uploads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/answerCallbackQuery")) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result = true }) };
            if (request.Content is MultipartFormDataContent) Uploads.Add(request.RequestUri.Segments.Last());
            else
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Messages.Add(body.GetProperty("text").GetString()!);
                if (body.TryGetProperty("reply_markup", out var markup))
                    foreach (var name in new[] { "keyboard", "inline_keyboard" })
                        if (markup.TryGetProperty(name, out var keyboard))
                            foreach (var button in keyboard.EnumerateArray().SelectMany(x => x.EnumerateArray()))
                            {
                                Buttons.Add(button.GetProperty("text").GetString()!);
                                if (button.TryGetProperty("web_app", out var app))
                                {
                                    WebApps.Add(app.GetProperty("url").GetString()!);
                                    if (name == "keyboard") ReplyWebApps.Add(app.GetProperty("url").GetString()!);
                                }
                            }
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result = new { message_id = Messages.Count, date = 0, chat = new { id = 101, type = "private" }, text = "sent" } }) };
        }
    }
    private static ServiceProvider Build(StateFactory factory, HttpMessageHandler apiHandler, TelegramApi telegramHandler)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTelegramBotKit(options => options.Token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", new HttpClient(telegramHandler, false))
            .UseMiddleware<PrivateStudentChat>();
        services.AddSingleton(new HttpClient(apiHandler, false) { BaseAddress = new Uri("http://school.test") });
        services.AddSingleton<IDbContextFactory<BotDbContext>>(factory);
        services.AddSingleton(_ => new ExerciseAudio(new HttpClient(new AudioStorage())));
        services.AddStudentCommands();
        return services.BuildServiceProvider();
    }

    private static Update Update(int id, string text, ChatType type = ChatType.Private) => new()
    {
        Id = id, Message = new Message { Id = id, Text = text, Chat = new Chat { Id = 101, Type = type }, From = new User { Id = 101, FirstName = "Student" } }
    };

    private static Update Callback(int id, string data) => new()
    {
        Id = id, CallbackQuery = new CallbackQuery { Id = "callback-" + id, Data = data,
            From = new User { Id = 101, FirstName = "Student" },
            Message = new Message { Id = 500, Chat = new Chat { Id = 101, Type = ChatType.Private }, From = new User { Id = 999, IsBot = true, FirstName = "Bot" } } }
    };

    private sealed class StateFactory(SqliteConnection connection) : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options);
    }

    private sealed class AudioStorage : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.False(request.Headers.Contains("X-Internal-Key"));
            Assert.Equal("https://storage.test/audio.wav?signature=test", request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("RIFF fake WAV") };
            response.Content.Headers.ContentType = new("audio/wav");
            return Task.FromResult(response);
        }
    }
}
