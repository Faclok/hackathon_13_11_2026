using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Host;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class PublicVerifiedTaskTests
{
    [Fact]
    public async Task AnonymousCatalogExposesOnlyExpertDecisionsAndReusesTts()
    {
        using var factory = new CoreFactory();
        var audio = new FakeAudio();
        using var customized = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISpeechSynthesizer>();
            services.RemoveAll<IAudioStorage>();
            services.AddSingleton<ISpeechSynthesizer>(audio);
            services.AddSingleton<IAudioStorage>(audio);
        }));
        using var anonymous = customized.CreateClient();
        using (var scope = customized.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await db.Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync("admin", Password, default);
        }
        using var admin = customized.CreateClient();
        await Login(admin, "admin");
        var verified = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
            new ExpertDatasetRow("verified-1", "Һаумыһығыҙ!", "ba-ru", "Здравствуйте!", true,
                new SourceReference("phrasebook", "Разговорник")));
        var pending = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports",
            new ExpertDatasetRow("pending-1", "Хәйерле көн!", "ba-ru", null, false,
                new SourceReference("phrasebook")));

        var list = await anonymous.GetFromJsonAsync<VerifiedTaskResponse[]>("/api/public/verified-tasks", Json);
        var task = Assert.Single(list!);
        Assert.Equal(verified.CaseId, task.CaseId);
        Assert.Equal("Һаумыһығыҙ!", task.SourceText);
        Assert.Equal("phrasebook", task.Source.GetProperty("document_id").GetString());
        Assert.Equal("Здравствуйте!", task.Decision.PrimaryTranslation);
        Assert.Contains(task.Variants, v => v.Id == task.Decision.PrimaryVariantId && v.Accepted);
        Assert.Null(task.Tts.AudioUrl);
        Assert.Equal(task.CaseId, (await anonymous.GetFromJsonAsync<VerifiedTaskResponse>(
            "/api/public/verified-tasks/random", Json))!.CaseId);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/verified-tasks/{pending.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync($"/api/public/verified-tasks/{pending.CaseId}/tts", new { })).StatusCode);
        Assert.Equal(0, audio.Syntheses);

        var first = await Post<VerifiedTaskTts>(anonymous, $"/api/public/verified-tasks/{verified.CaseId}/tts", new { });
        var second = await Post<VerifiedTaskTts>(anonymous, $"/api/public/verified-tasks/{verified.CaseId}/tts", new { });
        Assert.Equal(first.AudioId, second.AudioId);
        Assert.StartsWith("https://audio.test/", first.AudioUrl);
        Assert.Equal("audio/wav", first.ContentType);
        Assert.Equal(1, audio.Syntheses);
        Assert.Equal(first.AudioId, (await anonymous.GetFromJsonAsync<VerifiedTaskResponse>(
            $"/api/public/verified-tasks/{verified.CaseId}", Json))!.Tts.AudioId);
    }

    private sealed class FakeAudio : ISpeechSynthesizer, IAudioStorage
    {
        private readonly HashSet<string> keys = [];
        public int Syntheses { get; private set; }
        public Task<SynthesizedSpeech> SynthesizeAsync(string text, string language, CancellationToken ct)
        {
            Syntheses++;
            return Task.FromResult(new SynthesizedSpeech([1, 2, 3], "audio/wav", ".wav"));
        }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(keys.Contains(key));
        public Task UploadAsync(string key, SynthesizedSpeech speech, CancellationToken ct)
        {
            keys.Add(key);
            return Task.CompletedTask;
        }
        public Task<string> ListenUrlAsync(string key, CancellationToken ct) => Task.FromResult("https://audio.test/" + key);
    }
}
