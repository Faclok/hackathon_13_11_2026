using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class TelegramMiniAppTests
{
    private const string Token = "123456:test-bot-token-for-mini-app-tests";
    private const string Key = "test-internal-key";

    private static string Signed(long id, DateTimeOffset date, string token = Token, string? user = null)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = date.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["query_id"] = "query-for-tests", ["signature"] = "signed-telegram-field",
            ["user"] = user ?? JsonSerializer.Serialize(new { id, first_name = "Ученик" })
        };
        var secret = HMACSHA256.HashData("WebAppData"u8, Encoding.UTF8.GetBytes(token));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(x => x.Key + "=" + x.Value))));
        return string.Join('&', fields.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))) + "&hash=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task SignedLaunchUsesLinkedAccountAndUnlinkRevokesMiniAppButKeepsWebsiteSession()
    {
        using var f = new CoreFactory(telegramKey: Key, telegramToken: Token, learningKind: "translation-text");
        using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        await LearningContent(admin);
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { link.Code })).StatusCode);
        using var mini = f.CreateClient();
        var session = await Post<AccessTokenResponse>(mini, "/auth/telegram-mini-app", new { initData = Signed(101, f.Clock.Now) });
        mini.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var own = await mini.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(student.Id, own.GetProperty("id").GetGuid());
        var level = await CurrentLevel(student.Client);
        var fromMini = await CurrentLevel(mini);
        var fromBot = await Post<LevelResponse>(bot, "/internal/telegram/101/learning/current", new { });
        Assert.Equal(level.Question.AttemptToken, fromMini.Question.AttemptToken);
        Assert.Equal(level.Question.Id, fromBot.Question.Id);
        Assert.Equal(HttpStatusCode.OK, (await mini.GetAsync("/api/leaderboard?period=global&metric=tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.DeleteAsync("/api/me/telegram-link")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mini.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mini.PostAsJsonAsync("/auth/refresh", new { session.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.Client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mini.PostAsJsonAsync("/auth/telegram-mini-app", new { initData = Signed(101, f.Clock.Now) })).StatusCode);
    }

    [Theory]
    [InlineData("tampered-user")]
    [InlineData("wrong-bot")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("duplicate-field")]
    [InlineData("malformed-user")]
    [InlineData("non-object-user")]
    [InlineData("non-numeric-id")]
    [InlineData("bad-hash")]
    public async Task InvalidLaunchCannotOpenSession(string scenario)
    {
        using var f = new CoreFactory(telegramToken: Token);
        using var admin = await f.Bootstrap(); using var client = f.CreateClient();
        var data = scenario switch
        {
            "wrong-bot" => Signed(101, f.Clock.Now, "other-token"),
            "expired" => Signed(101, f.Clock.Now.AddSeconds(-301)),
            "future" => Signed(101, f.Clock.Now.AddSeconds(31)),
            "malformed-user" => Signed(101, f.Clock.Now, user: "{"),
            "non-object-user" => Signed(101, f.Clock.Now, user: "[]"),
            "non-numeric-id" => Signed(101, f.Clock.Now, user: "{\"id\":\"101\"}"),
            _ => Signed(101, f.Clock.Now)
        };
        if (scenario == "tampered-user") data = data.Replace("101", "102", StringComparison.Ordinal);
        if (scenario == "duplicate-field") data += "&auth_date=0";
        if (scenario == "bad-hash") data = data[..^64] + new string('z', 64);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/telegram-mini-app", new { initData = data })).StatusCode);
        using var scope = f.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Set<AccountSession>().CountAsync());
    }

    [Fact]
    public async Task ValidUnlinkedLaunchDoesNotRegisterAccountAndMissingTokenDisablesLogin()
    {
        using var f = new CoreFactory(telegramToken: Token); using var admin = await f.Bootstrap();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/auth/telegram-mini-app", new { initData = Signed(999, f.Clock.Now) })).StatusCode);
        using var scope = f.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Users.CountAsync());
        using var disabled = new CoreFactory(); using var admin2 = await disabled.Bootstrap();
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.CreateClient().PostAsJsonAsync("/auth/telegram-mini-app", new { initData = Signed(999, disabled.Clock.Now) })).StatusCode);
    }

    [Fact]
    public async Task BlockedLinkedAccountCannotSignIn()
    {
        using var f = new CoreFactory(telegramKey: Key, telegramToken: Token);
        using var admin = await f.Bootstrap(); var student = await f.Student(admin);
        var link = await Post<TelegramLinkResponse>(student.Client, "/api/me/telegram-link", new { });
        using var bot = f.CreateClient(); bot.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.NoContent, (await bot.PostAsJsonAsync("/internal/telegram/101/link", new { link.Code })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            (await db.Users.SingleAsync(x => x.Id == student.Id)).Active = false;
            await db.SaveChangesAsync();
        }
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/telegram-mini-app", new { initData = Signed(101, f.Clock.Now) })).StatusCode);
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(30)]
    public async Task ValidTimestampAtAcceptedBoundaryReachesLinkLookup(int seconds)
    {
        using var f = new CoreFactory(telegramToken: Token); using var admin = await f.Bootstrap();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/auth/telegram-mini-app", new { initData = Signed(999, f.Clock.Now.AddSeconds(seconds)) })).StatusCode);
    }
}
