using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class PlayerEconomyTests
{
    [Fact]
    public void CasePoolsMatchClientAndExcludeRetiredPetsAndDefaults()
    {
        Assert.Equal(["case_hat", "case_frame", "case_bg", "case_pet"], RewardCatalog.Cases.Select(x => x.Id));
        var petCase = RewardCatalog.Describe(RewardCatalog.Cases[3]);
        Assert.Equal(18, petCase.Drops.Length);
        Assert.Contains(petCase.Drops, x => x.DecorationId == "prize_pet_akbuzat");
        Assert.Contains(petCase.Drops, x => x.DecorationId == "prize_item_kurai");
        Assert.DoesNotContain(petCase.Drops, x => x.DecorationId is "pet_bee" or "pet_none");
        Assert.InRange(petCase.Drops.Sum(x => x.ChancePercent), 99.999m, 100.001m);
    }

    [Fact]
    public async Task ReviewRewardBuysKeyAndCaseOpeningPersistsInventoryAndEquipment()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        using var producer = await f.Producer(admin);
        var initial = await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile");
        Assert.Equal(0, initial!.Coins);
        Assert.Contains("bg_default", initial.OwnedDecorationIds);
        Assert.Equal(HttpStatusCode.Conflict,
            (await student.Client.PostAsJsonAsync("/api/player/keys", new { quantity = 1 })).StatusCode);

        await Deliver(producer, "first");
        await Deliver(producer, "second");
        for (var i = 0; i < 2; i++)
        {
            var assignment = await Post<AssignmentResponse>(student.Client, "/api/reviews/next", new { });
            var request = new SubmitReviewRequest(ReviewAction.Confirm);
            await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", request);
            await Post<ReviewReceipt>(student.Client, $"/api/reviews/{assignment.Id}/response", request);
        }
        var rewarded = await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile");
        Assert.Equal(10, rewarded!.Coins);
        Assert.Equal(20, rewarded.Xp);
        Assert.Equal(2, rewarded.TasksSolved);
        Assert.Contains("first", rewarded.TrophyIds);

        var bought = await Post<PlayerProfile>(student.Client, "/api/player/keys", new { quantity = 1 });
        Assert.Equal(0, bought.Coins);
        Assert.Equal(1, bought.CaseKeys);
        var opening = await Post<PlayerCaseOpening>(student.Client, "/api/player/cases/case_hat/open", new { });
        Assert.Equal("case_hat", opening.CaseId);
        Assert.Contains(opening.DecorationId, RewardCatalog.Drops(RewardCatalog.Cases[0]).Select(x => x.Id));
        var after = await student.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile");
        Assert.Equal(0, after!.CaseKeys);
        Assert.Contains(opening.DecorationId, after.OwnedDecorationIds);
        Assert.Equal(opening.Id, Assert.Single((await student.Client.GetFromJsonAsync<PlayerCaseOpening[]>("/api/player/cases/history"))!).Id);
        var equipped = await Put<PlayerProfile>(student.Client, $"/api/player/equipment/{opening.DecorationId}", new { equipped = true });
        Assert.Equal(opening.DecorationId, equipped.HatId);
    }

    [Fact]
    public async Task DuplicateReturnsThreeCoinsAndLeaderboardUsesRealParticipants()
    {
        using var f = new CoreFactory();
        using var admin = await f.Bootstrap();
        var first = await f.Student(admin);
        var second = await f.Student(admin, "2");
        using var producer = await f.Producer(admin);
        await Deliver(producer);
        var assignment = await Post<AssignmentResponse>(first.Client, "/api/reviews/next", new { });
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{assignment.Id}/response", new SubmitReviewRequest(ReviewAction.Confirm));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var state = await db.PlayerStates.SingleAsync(x => x.ParticipantId == first.Id);
            state.CaseKeys = 1;
            state.OwnedJson = JsonSerializer.Serialize(RewardCatalog.Drops(RewardCatalog.Cases[0]).Select(x => x.Id));
            await db.SaveChangesAsync();
        }
        var opening = await Post<PlayerCaseOpening>(first.Client, "/api/player/cases/case_hat/open", new { });
        Assert.True(opening.Duplicate);
        Assert.Equal(3, opening.Compensation);
        Assert.Equal(13, (await first.Client.GetFromJsonAsync<PlayerProfile>("/api/player/profile"))!.Coins);
        var board = await first.Client.GetFromJsonAsync<PlayerLeaderboardEntry[]>("/api/leaderboard?period=global&metric=tasks");
        Assert.Equal(first.Id, board![0].User.Id);
        Assert.Equal(1, board[0].Value);
        Assert.Contains(board, x => x.User.Id == second.Id && x.Value == 0);
    }

    private static async Task<T> Put<T>(HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
