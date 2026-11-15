using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class LevelAudioTests
{
    [Theory]
    [InlineData("audio-dictation", "Һаумы")]
    [InlineData("audio-translation", "Hello")]
    public async Task LevelAutomaticallyPreparesAudioAndReusesItAcrossQuestionsAndLevels(string kind, string answer)
    {
        using var factory = new CoreFactory(learningKind: kind);
        var audio = new FakeAudio();
        using var app = Customize(factory, audio);
        var clients = await Initialize(app);
        using var admin = clients.Admin;
        using var student = clients.Student;
        await LearningContent(admin);
        Assert.Equal(0, audio.Syntheses);

        var first = await CurrentLevel(student);
        Assert.Equal(kind, first.Question.Kind);
        var prompt = ExerciseJson.Read<ChoiceAudioPrompt>(first.Question.Prompt);
        Assert.NotEqual(Guid.Empty, prompt.AudioId);
        Assert.StartsWith("https://audio.test/", prompt.AudioUrl);
        Assert.Equal("Һаумы", prompt.Original);
        Assert.Equal(3, prompt.Options.Count);
        Assert.Equal(first.Question.Id, (await CurrentLevel(student)).Question.Id);
        for (var i = 0; i < first.QuestionCount; i++)
        {
            var current = await CurrentLevel(student);
            Assert.Equal(first.Id, current.Id);
            Assert.Equal(prompt.AudioId, ExerciseJson.Read<ChoiceAudioPrompt>(current.Question.Prompt).AudioId);
            var result = await AnswerLevel(student, current, new TextExerciseAnswer(answer));
            Assert.True(result.Recognized);
            Assert.Equal(i == first.QuestionCount - 1, result.LevelCompleted);
        }
        var second = await CurrentLevel(student);
        Assert.Equal(2, second.Number);
        Assert.Equal(prompt.AudioId, ExerciseJson.Read<ChoiceAudioPrompt>(second.Question.Prompt).AudioId);
        Assert.Equal(1, audio.Syntheses);
        Assert.Equal(1, audio.Uploads);
        using var scope = app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Set<SentenceAudio>().ToArrayAsync());
    }

    [Theory]
    [InlineData("audio-dictation")]
    [InlineData("audio-translation")]
    public async Task LevelPreparesOnlySelectedAudioInsteadOfTheWholeCorpus(string kind)
    {
        using var factory = new CoreFactory(learningKind: kind);
        var audio = new FakeAudio();
        using var app = Customize(factory, audio);
        var clients = await Initialize(app);
        using var admin = clients.Admin;
        using var student = clients.Student;
        for (var i = 0; i < 12; i++)
            await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
                new ExpertDatasetRow("audio-material-" + i, "Һөйләм " + i, "ba-ru", "Sentence " + i, true, new SourceReference("test")));

        var level = await CurrentLevel(student);
        Assert.Equal(kind, level.Question.Kind);
        Assert.Equal(level.QuestionCount, audio.Syntheses);
        Assert.Equal(audio.Syntheses, audio.Uploads);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(12, await db.Cases.CountAsync());
        Assert.Equal(audio.Syntheses, await db.Set<SentenceAudio>().CountAsync());
        var questions = await db.Set<LevelQuestion>().Where(x => x.LevelId == level.Id).ToArrayAsync();
        Assert.Equal(level.QuestionCount, questions.Select(x => x.CaseId).Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AudioFailureFallsBackToTextAndNextLevelRetriesPreparation(bool failSynthesis)
    {
        using var factory = new CoreFactory(learningKind: "audio-dictation");
        var audio = new FakeAudio { FailSynthesis = failSynthesis, FailUpload = !failSynthesis };
        using var app = Customize(factory, audio, mixed: true);
        var clients = await Initialize(app);
        using var admin = clients.Admin;
        using var student = clients.Student;
        await LearningContent(admin);

        var level = await CurrentLevel(student);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.Empty(await db.Set<SentenceAudio>().ToArrayAsync());
            var questions = await db.Set<LevelQuestion>().Where(x => x.LevelId == level.Id).ToArrayAsync();
            Assert.Equal(level.QuestionCount, questions.Length);
            Assert.All(questions, q => Assert.Equal("translation-text", q.Kind));
        }
        Assert.Equal(1, audio.Syntheses);
        Assert.Equal(0, audio.Uploads);
        for (var i = 0; i < level.QuestionCount; i++)
            Assert.True((await AnswerLevel(student, await CurrentLevel(student), new TextExerciseAnswer("Hello"))).Recognized);

        audio.FailSynthesis = audio.FailUpload = false;
        var next = await CurrentLevel(student);
        Assert.Equal(2, next.Number);
        using (var scope = app.Services.CreateScope())
        {
            var questions = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Set<LevelQuestion>()
                .Where(x => x.LevelId == next.Id && x.Kind == "audio-dictation").ToArrayAsync();
            Assert.NotEmpty(questions);
            Assert.All(questions, q =>
            {
                using var prompt = System.Text.Json.JsonDocument.Parse(q.PromptJson);
                Assert.NotEqual(Guid.Empty, ExerciseJson.Read<ChoiceAudioPrompt>(prompt.RootElement).AudioId);
            });
        }
        Assert.Equal(2, audio.Syntheses);
        Assert.Equal(1, audio.Uploads);
    }

    [Fact]
    public async Task FailedAudioOnlyLevelStaysEmptyAndCanBeRetriedWithoutAdvancingProgress()
    {
        using var factory = new CoreFactory(learningKind: "audio-dictation");
        var audio = new FakeAudio { FailUpload = true };
        using var app = Customize(factory, audio);
        var clients = await Initialize(app);
        using var admin = clients.Admin;
        using var student = clients.Student;
        await LearningContent(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync("/api/learning/current", new { })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.Empty(await db.Set<LearningLevel>().ToArrayAsync());
            Assert.Empty(await db.Set<SentenceAudio>().ToArrayAsync());
        }
        audio.FailUpload = false;
        var level = await CurrentLevel(student);
        Assert.Equal(1, level.Number);
        Assert.Equal("audio-dictation", level.Question.Kind);
        Assert.Equal(2, audio.Syntheses);
        Assert.Equal(1, audio.Uploads);
    }

    private static WebApplicationFactory<Program> Customize(CoreFactory factory, FakeAudio audio, bool mixed = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (mixed) builder.UseSetting("Learning:Kinds:0", "translation-text");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISpeechSynthesizer>();
                services.RemoveAll<IAudioStorage>();
                services.AddSingleton<ISpeechSynthesizer>(audio);
                services.AddSingleton<IAudioStorage>(audio);
            });
        });

    private static async Task<(HttpClient Admin, HttpClient Student)> Initialize(WebApplicationFactory<Program> app)
    {
        using var anonymous = app.CreateClient();
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync("admin", Password, default);
        }
        var admin = app.CreateClient();
        await Login(admin, "admin");
        await Post<AccountResponse>(anonymous, "/auth/register", new RegisterAccountRequest("audio-student", Password));
        var student = app.CreateClient();
        await Login(student, "audio-student");
        return (admin, student);
    }

    private sealed class FakeAudio : ISpeechSynthesizer, IAudioStorage
    {
        private readonly HashSet<string> keys = [];
        public int Syntheses { get; private set; }
        public int Uploads { get; private set; }
        public bool FailSynthesis { get; set; }
        public bool FailUpload { get; set; }
        public Task<SynthesizedSpeech> SynthesizeAsync(string text, string language, CancellationToken ct)
        {
            Syntheses++;
            if (FailSynthesis) throw new InvalidOperationException("Speech worker failed.");
            return Task.FromResult(new SynthesizedSpeech([1, 2, 3], "audio/wav", ".wav"));
        }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(keys.Contains(key));
        public Task UploadAsync(string key, SynthesizedSpeech speech, CancellationToken ct)
        {
            if (FailUpload) throw new IOException("Storage is unavailable.");
            Uploads++;
            keys.Add(key);
            return Task.CompletedTask;
        }
        public Task<string> ListenUrlAsync(string key, CancellationToken ct) => Task.FromResult("https://audio.test/" + key);
    }
}
