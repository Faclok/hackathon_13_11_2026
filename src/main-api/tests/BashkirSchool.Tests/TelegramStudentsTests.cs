using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class TelegramStudentsTests
{
    private const string Key = "test-only-telegram-internal-key";

    [Fact]
    public async Task BotRequiresKeyAndSingleUseLinkAndCannotUseStudentTokenAsBotCredentials()
    {
        using var f = new CoreFactory(telegramKey: Key);
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        using var bot = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.Client.GetAsync("/internal/telegram/101/profile")).StatusCode);
        bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.NotFound, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        Assert.InRange(link.Code.Length, 32, 64);
        Assert.Equal(f.Clock.Now.AddMinutes(10), link.ExpiresAt);
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { link.Code })).StatusCode);
        var profile = await bot.GetFromJsonAsync<MyProfileResponse>("/internal/telegram/101/profile");
        Assert.Equal(student.Id, profile!.Id);
        Assert.Equal(student.ClassId, profile.ClassId);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/internal/telegram/102/link", new { link.Code })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bot.GetAsync("/internal/telegram/102/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/api/schools")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/me/telegram-link", new { })).StatusCode);
    }

    [Fact]
    public async Task ExpiredAndReplacedLinksAreRejectedAndUnlinkRevokesBothBindingAndPendingCode()
    {
        using var f = new CoreFactory(telegramKey: Key);
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        var old = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        var current = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { old.Code })).StatusCode);
        f.Clock.Now = current.ExpiresAt;
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { current.Code })).StatusCode);
        var valid = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { valid.Code })).StatusCode);
        var pending = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync("/internal/telegram/102/link", new { pending.Code })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.DeleteAsync("/api/me/telegram-link")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/internal/telegram/102/link", new { pending.Code })).StatusCode);
    }

    [Fact]
    public async Task DailyAnswersBelongToLinkedStudentAndProgressPersistsAcrossWebsiteAndBot()
    {
        using var f = new CoreFactory(telegramKey: Key);
        using var admin = await f.Bootstrap(); var student = await f.Student(admin); var other = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        var words = new[] { new VocabularyItemRequest("бер", "один"), new("ике", "два"), new("өс", "три"), new("дүрт", "четыре"), new("биш", "пять") };
        await Post<VocabularyDeliveryResponse>(producer, "/api/vocabulary", new DeliverVocabularyRequest("ba-ru", words));
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        foreach (var (client, id) in new[] { (student.Client, 101), (other.Client, 102) })
        {
            var link = await Post<TelegramLinkResponse>(client, "/api/me/telegram-link", new { });
            Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync($"/internal/telegram/{id}/link", new { link.Code })).StatusCode);
        }
        var lesson = (await bot.GetFromJsonAsync<LessonResponse>("/internal/telegram/101/lesson"))!;
        var exercise = Assert.Single(lesson.Exercises);
        Assert.False(exercise.Completed);
        var prompt = ExerciseJson.Read<WordMatchPrompt>(exercise.Prompt);
        Assert.False(exercise.Prompt.TryGetProperty("pairs", out _));
        var pairs = prompt.Left.Select(l => new WordPairAnswer(l.Id, prompt.Right.Single(r => r.Text == words.Single(w => w.Word == l.Text).Translation).Id)).ToArray();
        var answer = new SubmitExerciseAnswerRequest(ExerciseJson.Element(new WordMatchAnswer(pairs)));
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PostAsJsonAsync($"/internal/telegram/102/exercises/{exercise.Id}/answer", answer)).StatusCode);
        var receipt = await Post<AttemptResponse>(bot, $"/internal/telegram/101/exercises/{exercise.Id}/answer", answer);
        Assert.True(receipt.Recognized);
        Assert.Equal(receipt.Id, (await Post<AttemptResponse>(bot, $"/internal/telegram/101/exercises/{exercise.Id}/answer", answer)).Id);
        var webLesson = (await student.Client.GetFromJsonAsync<LessonResponse>("/api/lessons/today"))!;
        Assert.Equal(lesson.Id, webLesson.Id);
        Assert.True(Assert.Single(webLesson.Exercises).Completed);
        var resumed = (await bot.GetFromJsonAsync<LessonResponse>("/internal/telegram/101/lesson"))!;
        Assert.True(Assert.Single(resumed.Exercises).Completed);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var account = await db.Users.SingleAsync(x => x.Id == student.Id); account.Active = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/lesson")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.PostAsJsonAsync($"/internal/telegram/101/exercises/{exercise.Id}/answer", answer)).StatusCode);
    }

    [Fact]
    public async Task MissingBotKeyDisablesInternalRoutes()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
    }
}
