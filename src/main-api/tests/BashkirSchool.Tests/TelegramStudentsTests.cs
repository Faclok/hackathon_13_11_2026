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
    public async Task LevelAnswersBelongToLinkedStudentAndProgressPersistsAcrossWebsiteAndBot()
    {
        using var f = new CoreFactory(telegramKey: Key, learningKind: "word-match");
        using var admin = await f.Bootstrap(); var student = await f.Student(admin); var other = await f.Student(admin, "2");
        await LearningContent(admin);
        using var producer = await f.Producer(admin);
        var words = new[] { new VocabularyItemRequest("бер", "один"), new("ике", "два"), new("өс", "три"), new("дүрт", "четыре"), new("биш", "пять") };
        await Post<VocabularyDeliveryResponse>(producer, "/api/vocabulary", new DeliverVocabularyRequest("ba-ru", words));
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        foreach (var (client, id) in new[] { (student.Client, 101), (other.Client, 102) })
        {
            var link = await Post<TelegramLinkResponse>(client, "/api/me/telegram-link", new { });
            Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync($"/internal/telegram/{id}/link", new { link.Code })).StatusCode);
        }
        var level = await Post<LevelResponse>(bot, "/internal/telegram/101/learning/current", new { });
        Assert.Equal(0, level.CorrectCount);
        var prompt = ExerciseJson.Read<WordMatchPrompt>(level.Question.Prompt);
        Assert.False(level.Question.Prompt.TryGetProperty("pairs", out _));
        var pairs = prompt.Left.Select(l => new WordPairAnswer(l.Id, prompt.Right.Single(r => r.Text == words.Single(w => w.Word == l.Text).Translation).Id)).ToArray();
        var answer = new AnswerLevelRequest(level.Question.AttemptToken, ExerciseJson.Element(new WordMatchAnswer(pairs)));
        var suffix = $"/learning/levels/{level.Id}/questions/{level.Question.Id}/answer";
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PostAsJsonAsync("/internal/telegram/102" + suffix, answer)).StatusCode);
        var receipt = await Post<LevelAnswerResponse>(bot, "/internal/telegram/101" + suffix, answer);
        Assert.True(receipt.Recognized);
        Assert.Equal(receipt, await Post<LevelAnswerResponse>(bot, "/internal/telegram/101" + suffix, answer));
        var webLevel = await CurrentLevel(student.Client);
        Assert.Equal(level.Id, webLevel.Id); Assert.Equal(1, webLevel.CorrectCount);
        Assert.NotEqual(level.Question.Id, webLevel.Question.Id);
        var resumed = await Post<LevelResponse>(bot, "/internal/telegram/101/learning/current", new { });
        Assert.Equal(webLevel.Question.Id, resumed.Question.Id); Assert.Equal(1, resumed.CorrectCount);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var account = await db.Users.SingleAsync(x => x.Id == student.Id); account.Active = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.PostAsJsonAsync("/internal/telegram/101/learning/current", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.PostAsJsonAsync("/internal/telegram/101" + suffix, answer)).StatusCode);
    }

    [Fact]
    public async Task MissingBotKeyDisablesInternalRoutes()
    {
        using var f = new CoreFactory(); using var admin = await f.Bootstrap();
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/internal/telegram/101/profile")).StatusCode);
    }

    [Fact]
    public async Task BotIssuesReviewAndOnlyLinkedOwnerCanSubmit()
    {
        using var f = new CoreFactory(telegramKey: Key);
        using var admin = await f.Bootstrap(); var student = await f.Student(admin); var other = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        await Deliver(producer);
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        foreach (var (client, id) in new[] { (student.Client, 101), (other.Client, 102) })
        {
            var link = await Post<TelegramLinkResponse>(client, "/api/me/telegram-link", new { });
            Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync($"/internal/telegram/{id}/link", new { link.Code })).StatusCode);
        }
        var review = await Post<AssignmentResponse>(bot, "/internal/telegram/101/reviews/next?kind=translation-choice", new { });
        Assert.Equal("translation-choice", review.Kind);
        var option = Assert.Single(review.Options);
        var answer = new SubmitReviewRequest(BashkirSchool.Domain.ReviewAction.Confirm) { SelectedVariantId = option.Id };
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PostAsJsonAsync($"/internal/telegram/102/reviews/{review.Id}/response", answer, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync($"/internal/telegram/101/reviews/{review.Id}/response",
            answer with { SelectedVariantId = Guid.NewGuid() }, Json)).StatusCode);
        var receipt = await Post<ReviewReceipt>(bot, $"/internal/telegram/101/reviews/{review.Id}/response", answer);
        Assert.Equal(receipt.ResponseId, (await Post<ReviewReceipt>(bot, $"/internal/telegram/101/reviews/{review.Id}/response", answer)).ResponseId);
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/learning/current", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PostAsJsonAsync("/internal/telegram/999/learning/current", new { })).StatusCode);
        using var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/internal/telegram/101/reviews/next", new { })).StatusCode);
    }
}
