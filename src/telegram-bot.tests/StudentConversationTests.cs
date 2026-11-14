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
    public async Task ChatResumesAfterRestartAndRedeliveryNeverAnswersTheNextExercise()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var apiHandler = new LessonApi(); var telegramHandler = new TelegramApi();
        await using (var provider = Build(factory, apiHandler, telegramHandler))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, "/daily"), default);
            await dispatcher.DispatchAsync(Update(2, "Беренсе"), default);
        }
        // Reconstruct all application scopes, as after a process restart.
        await using (var provider = Build(factory, apiHandler, telegramHandler))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(2, "Беренсе"), default);
            Assert.Equal(new[] { apiHandler.First }, apiHandler.Submissions);
            await dispatcher.DispatchAsync(Update(3, "Икенсе"), default);
        }
        Assert.Equal(new[] { apiHandler.First, apiHandler.Second }, apiHandler.Submissions);
        Assert.Contains(telegramHandler.Messages, x => x.Contains("выполнены: 2 из 2"));
        await using var state = factory.CreateDbContext();
        var final = await state.States.SingleAsync();
        Assert.Equal("idle", final.State); Assert.Null(final.TaskId);
    }

    [Fact]
    public async Task GroupMessagesNeverRequestStudentDataOrCreateConversationState()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new LessonApi(); var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        await provider.GetRequiredService<IUpdateDispatcher>().DispatchAsync(Update(1, "/daily", ChatType.Group), default);
        Assert.Equal(0, api.Requests); Assert.Empty(telegram.Messages);
        await using var state = factory.CreateDbContext(); Assert.Empty(await state.States.ToArrayAsync());
    }

    [Theory]
    [InlineData("Перевод текста", "translation-text", "Һаумы", """{"text":"Һаумы"}""")]
    [InlineData("Сопоставление слов", "word-match", "1-2 2-1", """{"pairs":[{"leftId":"a","rightId":"y"},{"leftId":"b","rightId":"x"}]}""")]
    [InlineData("Аудиодиктант", "audio-dictation", "Һаумы", """{"text":"Һаумы"}""")]
    [InlineData("Перевод аудио", "audio-translation", "Здравствуй", """{"text":"Здравствуй"}""")]
    [InlineData("Пропущенное слово", "missing-word", "2", """{"optionId":"b"}""")]
    [InlineData("Сборка предложения", "sentence-order", "2 1", """{"tokenIds":["b","a"]}""")]
    public async Task EveryExerciseKindCanBeSelectedDisplayedAndCompleted(string label, string kind, string text, string expected)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new IssuedExerciseApi(kind, expected); var telegram = new TelegramApi();
        await using var provider = Build(factory, api, telegram);
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        await dispatcher.DispatchAsync(Update(1, "Виды заданий"), default);
        Assert.Contains(label, telegram.Buttons);
        await dispatcher.DispatchAsync(Update(2, label), default);
        Assert.Contains(telegram.Buttons, x => x == "Виды заданий");
        Assert.Contains(telegram.Messages, x => x.Contains(kind switch
        {
            "word-match" => "Сопоставьте слова", "sentence-order" => "Соберите предложение",
            "missing-word" => "Заполните пропуск", "translation-text" => "Переведите текст", _ => "Прослушайте аудио"
        }));
        if (kind.StartsWith("audio-"))
        {
            Assert.Equal(new[] { "sendDocument" }, telegram.Uploads);
            Assert.DoesNotContain(telegram.Messages, x => x.Contains("секретный исходник"));
        }
        if (kind == "missing-word") Assert.Contains("2", telegram.Buttons);
        await dispatcher.DispatchAsync(Update(3, text), default);
        Assert.Equal(1, api.Submissions);
        Assert.Contains(telegram.Messages, x => x.Contains("выполнены: 1 из 1"));
    }

    [Theory]
    [InlineData("translation-choice", "2", false, "Confirm")]
    [InlineData("translation-choice", "2", true, "Correct")]
    [InlineData("translation-confirm", "Подтвердить перевод", false, "Confirm")]
    [InlineData("translation-confirm", "Приветствую!", true, "Correct")]
    [InlineData("translation-choice", "Пропустить перевод", false, "Skip")]
    public async Task ReviewModesPreserveIssuedOptionsAndCustomInputAcrossRestart(string kind, string text, bool custom, string action)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var factory = new StateFactory(connection);
        await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var api = new ReviewApi(kind); var telegram = new TelegramApi();
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(1, kind == "translation-choice" ? "Выбор перевода" : "Подтверждение перевода"), default);
            Assert.Contains(telegram.Messages, x => x.Contains("Һаумы!") && x.Contains("Здравствуй!"));
            Assert.Contains(kind == "translation-choice" ? "2" : "Подтвердить перевод", telegram.Buttons);
            if (custom) await dispatcher.DispatchAsync(Update(2, "Свой перевод"), default);
        }
        await using (var provider = Build(factory, api, telegram))
        {
            var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
            await dispatcher.DispatchAsync(Update(3, text), default);
            await dispatcher.DispatchAsync(Update(3, text), default);
        }
        var answer = Assert.Single(api.Answers);
        Assert.Equal(action, answer.GetProperty("action").GetString());
        Assert.Equal(custom ? text : null, answer.GetProperty("correction").GetString());
        Assert.Equal(kind == "translation-choice" && action == "Confirm" ? api.Second.ToString() : null, answer.GetProperty("selectedVariantId").GetString());
        Assert.Contains(telegram.Messages, x => x.Contains(action == "Skip" ? "Перевод пропущен" : "Ваш ответ сохранён"));
        Assert.DoesNotContain(telegram.Messages, x => x.Contains("верный"));
    }

    private static ServiceProvider Build(StateFactory factory, HttpMessageHandler apiHandler, TelegramApi telegramHandler)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTelegramBotKit(options => options.Token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", new HttpClient(telegramHandler, false));
        services.AddSingleton(new HttpClient(apiHandler, false) { BaseAddress = new Uri("http://school.test") });
        services.AddSingleton<IDbContextFactory<BotDbContext>>(factory);
        services.AddSingleton(_ => new ExerciseAudio(new HttpClient(new AudioStorage())));
        services.AddScoped<IDefaultMessageHandler, StudentConversation>();
        return services.BuildServiceProvider();
    }

    private static Update Update(int id, string text, ChatType type = ChatType.Private) => new()
    {
        Id = id, Message = new Message { Id = id, Text = text, Chat = new Chat { Id = 101, Type = type }, From = new User { Id = 101, FirstName = "Student" } }
    };

    private sealed class StateFactory(SqliteConnection connection) : IDbContextFactory<BotDbContext>
    {
        public BotDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options);
    }

    private sealed class LessonApi : HttpMessageHandler
    {
        public Guid First { get; } = Guid.NewGuid();
        public Guid Second { get; } = Guid.NewGuid();
        public List<Guid> Submissions { get; } = [];
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/internal/telegram/101/lesson")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Guid.Empty, date = new DateOnly(2026, 11, 14), exercises = new[] {
                    new { id = First, original = "Беренсе", kind = "translation-text", schemaVersion = 1, completed = Submissions.Contains(First), prompt = new { text = "Беренсе" } },
                    new { id = Second, original = "Икенсе", kind = "translation-text", schemaVersion = 1, completed = Submissions.Contains(Second), prompt = new { text = "Икенсе" } }
                } }) };
            if (request.Method == HttpMethod.Post && path.EndsWith("/answer"))
            {
                var id = Guid.Parse(path.Split('/')[5]);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal(id == First ? "Беренсе" : "Икенсе", body.GetProperty("answer").GetProperty("text").GetString());
                Submissions.Add(id);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Guid.NewGuid(), recognized = true }) };
            }
            throw new InvalidOperationException("Unexpected student API call: " + path);
        }
    }

    private sealed class TelegramApi : HttpMessageHandler
    {
        public List<string> Messages { get; } = [];
        public List<string> Buttons { get; } = [];
        public List<string> Uploads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is MultipartFormDataContent)
            {
                Uploads.Add(request.RequestUri!.Segments.Last());
                var upload = await request.Content.ReadAsStringAsync(ct);
                Assert.Contains("exercise.wav", upload); Assert.Contains("RIFF", upload);
            }
            else
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Messages.Add(body.GetProperty("text").GetString()!);
                if (body.TryGetProperty("reply_markup", out var markup))
                    Buttons.AddRange(markup.GetProperty("keyboard").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(x => x.GetProperty("text").GetString()!));
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result = new { message_id = Messages.Count, date = 0, chat = new { id = 101, type = "private" }, text = "sent" } }) };
        }
    }

    private sealed class ReviewApi(string kind) : HttpMessageHandler
    {
        private readonly Guid assignment = Guid.NewGuid();
        public Guid Second { get; } = Guid.NewGuid();
        public List<JsonElement> Answers { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/reviews/next"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("?kind=" + kind, request.RequestUri.Query);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ReviewView(assignment, Guid.NewGuid(), Guid.NewGuid(), "Һаумы!", "Здравствуй!", DateTimeOffset.UtcNow.AddMinutes(30), kind,
                    [new(Guid.NewGuid(), "Здравствуй!"), new(Second, "Привет!")], true)) };
            }
            Assert.Equal($"/internal/telegram/101/reviews/{assignment}/response", request.RequestUri.AbsolutePath);
            Answers.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(ct));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { responseId = Guid.NewGuid(), archived = false }) };
        }
    }

    private sealed class IssuedExerciseApi(string kind, string expected) : HttpMessageHandler
    {
        public int Submissions { get; private set; }
        private readonly Guid id = Guid.NewGuid();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/answer"))
            {
                Assert.Equal($"/internal/telegram/101/exercises/{id}/answer", path);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, body.GetProperty("answer")));
                Submissions++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Guid.NewGuid(), recognized = true }) };
            }
            var prompt = JsonDocument.Parse(kind switch
            {
                "word-match" => """{"left":[{"id":"a","text":"бер"},{"id":"b","text":"ике"}],"right":[{"id":"x","text":"два"},{"id":"y","text":"один"}]}""",
                "missing-word" => """{"before":"Һаумы, ","after":"!","options":[{"id":"a","text":"донъя"},{"id":"b","text":"мәктәп"}],"allowCustom":true}""",
                "sentence-order" => """{"tokens":[{"id":"a","text":"донъя!"},{"id":"b","text":"Һаумы,"}]}""",
                "audio-dictation" or "audio-translation" => """{"audioUrl":"https://storage.test/audio.wav?signature=test","audioId":"00000000-0000-0000-0000-000000000001"}""",
                _ => """{"text":"Һаумы!","sourceLanguage":"ba","targetLanguage":"ru"}"""
            }).RootElement.Clone();
            var exercise = new ExerciseView(id, "", Submissions > 0, kind, 1, prompt);
            if (path.EndsWith("/lesson/exercises"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal(kind, body.GetProperty("kind").GetString());
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(exercise) };
            }
            Assert.Equal("/internal/telegram/101/lesson", path);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Guid.NewGuid(), date = new DateOnly(2026, 11, 14), exercises = new[] { exercise } }) };
        }
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
