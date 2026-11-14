using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ExerciseWorkflowTests
{
    [Fact]
    public async Task WordPairsAreImportedIdempotentlyIssuedPrivatelyAndAnsweredOnlyByOwner()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin); var other = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        var entries = new[] { new VocabularyItemRequest("бер", "один"), new("ике", "два"), new("өс", "три"), new("дүрт", "четыре"), new("биш", "пять") };
        var input = new DeliverVocabularyRequest("ba-ru", entries);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.PostAsJsonAsync("/api/vocabulary", input)).StatusCode);
        var first = await Post<VocabularyDeliveryResponse>(producer, "/api/vocabulary", input);
        Assert.Equal(first.Ids, (await Post<VocabularyDeliveryResponse>(producer, "/api/vocabulary", input)).Ids);
        var exercise = await Post<ExerciseResponse>(student.Client, "/api/lessons/today/exercises", new IssueExerciseRequest("word-match"));
        var again = await Post<ExerciseResponse>(student.Client, "/api/lessons/today/exercises", new IssueExerciseRequest("word-match"));
        Assert.Equal(exercise.Id, again.Id); Assert.Equal(exercise.Prompt.GetRawText(), again.Prompt.GetRawText());
        Assert.False(exercise.Prompt.TryGetProperty("pairs", out _));
        var prompt = ExerciseJson.Read<WordMatchPrompt>(exercise.Prompt);
        var pairs = prompt.Left.Select(l => new WordPairAnswer(l.Id, prompt.Right.Single(r => r.Text == entries.Single(e => e.Word == l.Text).Translation).Id)).ToArray();
        var answer = new SubmitExerciseAnswerRequest(ExerciseJson.Element(new WordMatchAnswer(pairs)));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/exercises/{exercise.Id}/answer", answer)).StatusCode);
        var result = await Post<AttemptResponse>(student.Client, $"/api/exercises/{exercise.Id}/answer", answer);
        Assert.True(result.Recognized);
        Assert.Equal(result.Id, (await Post<AttemptResponse>(student.Client, $"/api/exercises/{exercise.Id}/answer", answer)).Id);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(5, await db.Set<VocabularyEntry>().CountAsync()); Assert.Single(await db.Set<ExerciseAttempt>().ToArrayAsync());
        Assert.False(await db.Judgments.AnyAsync());
        Assert.All(await db.Participants.ToArrayAsync(), p => Assert.Equal((0, 0), (p.Correct, p.Total)));
    }

    [Fact]
    public async Task ReviewChoicesAreTopFourSnapshotAndSelectedVariantGetsTheOnlyVote()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var voter = await f.Student(admin); var student = await f.Student(admin, "2");
        using var producer = await f.Producer(admin); var delivery = await Deliver(producer);
        Guid popular;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var variants = Enumerable.Range(0, 5).Select(i => TranslationVariant.Create(delivery.CaseId, "candidate" + i, false, createdAt: f.Clock.Now.AddSeconds(i))).ToArray();
            db.Variants.AddRange(variants); popular = variants[4].Id;
            var c = await db.Cases.SingleAsync(); var policy = await db.Policies.SingleAsync();
            var a = ReviewAssignment.Issue(voter.Id, c, null, null, policy.Id, f.Clock.Now, TimeSpan.FromMinutes(30), popular);
            var response = ReviewResponse.Submit(a, voter.Id, ReviewAction.Confirm, null, .5m, f.Clock.Now, false);
            db.AddRange(a, response); db.Judgments.AddRange(response.Judgments(popular, null)); await db.SaveChangesAsync();
        }
        var issued = await Post<AssignmentResponse>(student.Client, "/api/reviews/next?kind=translation-choice", new { });
        Assert.Equal("translation-choice", issued.Kind); Assert.Equal(4, issued.Options.Count); Assert.Equal(popular, issued.Options[0].Id);
        var selected = issued.Options[2].Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync($"/api/reviews/{issued.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm) { SelectedVariantId = Guid.NewGuid() }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync($"/api/reviews/{issued.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm), Json)).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            db.Variants.Add(TranslationVariant.Create(delivery.CaseId, "new later candidate", false)); await db.SaveChangesAsync();
        }
        var resumed = await Post<AssignmentResponse>(student.Client, "/api/reviews/next?kind=translation-choice", new { });
        Assert.Equal(issued.Options, resumed.Options); Assert.Equal(issued.Id, resumed.Id);
        var input = new SubmitReviewRequest(ReviewAction.Confirm) { SelectedVariantId = selected };
        var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{issued.Id}/response", input);
        Assert.Equal(receipt.ResponseId, (await Post<ReviewReceipt>(student.Client, $"/api/reviews/{issued.Id}/response", input)).ResponseId);
        Assert.Equal(HttpStatusCode.Conflict, (await student.Client.PostAsJsonAsync($"/api/reviews/{issued.Id}/response", input with { SelectedVariantId = popular }, Json)).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Assert.Equal(selected, (await db.Judgments.SingleAsync(x => x.ResponseId == receipt.ResponseId)).VariantId);
            Assert.Equal(4, (await db.Assignments.SingleAsync(x => x.Id == issued.Id)).ShownVariants.Length);
            await scope.ServiceProvider.GetRequiredService<ResolutionProcessor>().ProcessAsync(delivery.CaseId, default);
        }
    }

    [Fact]
    public async Task SingleChoiceAndCustomTranslationRemainOneAcceptance()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var producer = await f.Producer(admin); await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next?kind=translation-choice", new { });
        Assert.Single(assignment.Options);
        var receipt = await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Correct, "Здравствуйте"));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var variantId = (await db.Judgments.SingleAsync(x => x.ResponseId == receipt.ResponseId)).VariantId;
        Assert.Equal("Здравствуйте", (await db.Variants.SingleAsync(x => x.Id == variantId)).Text);
        Assert.Single(await db.Judgments.ToArrayAsync());
    }

    [Fact]
    public async Task GapAndOrderSnapshotsSurviveRepeatedRequestsAndValidateAnswers()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        using var producer = await f.Producer(admin); var delivery = await Sentence(producer, "sentence", "Мин мәктәпкә барам.");
        var gap = await Post<ExerciseResponse>(student.Client, "/api/lessons/today/exercises", new IssueExerciseRequest("missing-word", delivery.CaseId));
        var same = await Post<ExerciseResponse>(student.Client, "/api/lessons/today/exercises", new IssueExerciseRequest("missing-word", delivery.CaseId));
        Assert.Equal(gap.Id, same.Id); Assert.Equal(gap.Prompt.GetRawText(), same.Prompt.GetRawText()); Assert.Equal("", gap.Original);
        var prompt = ExerciseJson.Read<GapExercisePrompt>(gap.Prompt);
        var correct = prompt.Options.Single(x => prompt.Before + x.Text + prompt.After == "Мин мәктәпкә барам.");
        Assert.True((await Post<AttemptResponse>(student.Client, $"/api/exercises/{gap.Id}/answer", new SubmitExerciseAnswerRequest(ExerciseJson.Element(new GapExerciseAnswer(OptionId: correct.Id))))).Recognized);
        var order = await Post<ExerciseResponse>(student.Client, "/api/lessons/today/exercises", new IssueExerciseRequest("sentence-order", delivery.CaseId));
        var tokens = ExerciseJson.Read<SentenceOrderPrompt>(order.Prompt).Tokens;
        var ids = new[] { "Мин", "мәктәпкә", "барам." }.Select(text => tokens.Single(x => x.Text == text).Id).ToArray();
        Assert.True((await Post<AttemptResponse>(student.Client, $"/api/exercises/{order.Id}/answer", new SubmitExerciseAnswerRequest(ExerciseJson.Element(new SentenceOrderAnswer(ids))))).Recognized);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.Client.PostAsJsonAsync("/api/lessons/today/exercises", new IssueExerciseRequest("unsupported"))).StatusCode);
    }

    [Fact]
    public async Task AudioIsLazyDeduplicatedAndTranslationWaitsForAnAcceptedRendering()
    {
        using var f = new CoreFactory(); var fake = new FakeAudio();
        using var customized = f.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ISpeechSynthesizer>(); s.RemoveAll<IAudioStorage>();
            s.AddSingleton<ISpeechSynthesizer>(fake); s.AddSingleton<IAudioStorage>(fake);
        }));
        // Initialize the customized factory's own relational database and normal authentication.
        using var anonymous = customized.CreateClient();
        using (var scope = customized.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync("admin", Password, default);
        }
        using var admin = customized.CreateClient(); await Login(admin, "admin");
        var account = await Post<AccountResponse>(anonymous, "/auth/register", new RegisterAccountRequest("audio-student", Password));
        using var student = customized.CreateClient(); await Login(student, account.Username);
        var producerAccount = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("audio-producer", "producer"));
        await Activate(admin, producerAccount.Id);
        using var producer = customized.CreateClient(); await Login(producer, producerAccount.Username);
        var delivery = await Sentence(producer, "audio", "Мин мәктәпкә барам.");
        Assert.Equal(0, fake.Syntheses);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync("/api/lessons/today/exercises", new IssueExerciseRequest("audio-translation", delivery.CaseId))).StatusCode);
        Assert.Equal(0, fake.Syntheses);
        var dictation = await Post<ExerciseResponse>(student, "/api/lessons/today/exercises", new IssueExerciseRequest("audio-dictation", delivery.CaseId));
        Assert.Equal(1, fake.Syntheses); Assert.Equal(1, fake.Uploads); Assert.Equal("", dictation.Original);
        var prompt = ExerciseJson.Read<AudioExercisePrompt>(dictation.Prompt);
        Assert.StartsWith("https://audio.test/", prompt.AudioUrl);
        Assert.Equal(dictation.Id, (await Post<ExerciseResponse>(student, "/api/lessons/today/exercises", new IssueExerciseRequest("audio-dictation", delivery.CaseId))).Id);
        var duplicate = await Sentence(producer, "same-audio", "Мин мәктәпкә барам.");
        await Post<AudioAssetResponse>(student, $"/api/cases/{duplicate.CaseId}/audio", new { });
        Assert.Equal(1, fake.Syntheses); Assert.Equal(1, fake.Uploads);
        using (var scope = customized.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>(); var c = await db.Cases.SingleAsync(x => x.Id == delivery.CaseId);
            var policy = await db.Policies.SingleAsync();
            var resolution = ResolutionRevision.Create(c.Id, 1, ResolutionKind.Community, delivery.VariantId, [delivery.VariantId], [], policy.Id, null, "{}", null, f.Clock.Now);
            c.Apply(resolution); db.Add(resolution); await db.SaveChangesAsync();
        }
        var translation = await Post<ExerciseResponse>(student, "/api/lessons/today/exercises", new IssueExerciseRequest("audio-translation", delivery.CaseId));
        Assert.True((await Post<AttemptResponse>(student, $"/api/exercises/{translation.Id}/answer", new SubmitExerciseAnswerRequest(ExerciseJson.Element(new TextExerciseAnswer("Я иду в школу."))))).Recognized);
        Assert.True((await Post<AttemptResponse>(student, $"/api/exercises/{dictation.Id}/answer", new SubmitExerciseAnswerRequest(ExerciseJson.Element(new TextExerciseAnswer("Мин мәктәпкә барам."))))).Recognized);
        Assert.Equal(1, fake.Syntheses);
        using (var scope = customized.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == delivery.CaseId); var policy = await db.Policies.SingleAsync();
            var pending = ResolutionRevision.Create(c.Id, 2, ResolutionKind.Pending, null, [], [], policy.Id, null, "{}", null, f.Clock.Now);
            c.Apply(pending); db.Add(pending); await db.SaveChangesAsync();
        }
        Assert.Equal(translation.Id, (await Post<ExerciseResponse>(student, "/api/lessons/today/exercises", new IssueExerciseRequest("audio-translation", delivery.CaseId))).Id);
        var failing = await Sentence(producer, "failure", "Башҡа һөйләм."); fake.FailUpload = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await student.PostAsJsonAsync($"/api/cases/{failing.CaseId}/audio", new { })).StatusCode);
        using (var scope = customized.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Set<SentenceAudio>().AnyAsync(x => x.FragmentId == failing.FragmentId));
        fake.FailUpload = false;
        await Post<AudioAssetResponse>(student, $"/api/cases/{failing.CaseId}/audio", new { });
    }

    private static async Task<DeliveryResponse> Sentence(HttpClient producer, string key, string original)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/deliveries"); request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(new DeliverContentRequest(key, original, "ba-ru", "Я иду в школу.", new("test-document")));
        var response = await producer.SendAsync(request); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeliveryResponse>())!;
    }

    private sealed class FakeAudio : ISpeechSynthesizer, IAudioStorage
    {
        public int Syntheses { get; private set; }
        public int Uploads { get; private set; }
        public bool FailUpload { get; set; }
        private readonly HashSet<string> keys = [];
        public Task<SynthesizedSpeech> SynthesizeAsync(string text, string language, CancellationToken ct)
        { Syntheses++; return Task.FromResult(new SynthesizedSpeech([1, 2, 3], "audio/wav", ".wav")); }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(keys.Contains(key));
        public Task UploadAsync(string key, SynthesizedSpeech speech, CancellationToken ct)
        { if (FailUpload) throw new IOException("Storage is unavailable."); Uploads++; keys.Add(key); return Task.CompletedTask; }
        public Task<string> ListenUrlAsync(string key, CancellationToken ct) => Task.FromResult("https://audio.test/" + key);
    }
}
