using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class PlayerLevelRewardsTests
{
    [Fact]
    public async Task ClaimsEveryReachedLevelWithMilestoneRarityAndPersistsOnlyOnce()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        var initial = await Claim(student.Client);
        Assert.Equal(1, initial.Profile.Level);
        Assert.Empty(initial.Rewards);
        await SetState(factory, student.Id, 3750);

        var receipt = await Claim(student.Client);
        Assert.Equal(Enumerable.Range(2, 10), receipt.Rewards.Select(x => x.Level));
        Assert.Equal(new string?[] { "common", null, "common", "rare", "common", null, "common", null, "epic", null },
            receipt.Rewards.Select(x => x.Rarity));
        Assert.Equal(3750, receipt.Profile.Xp);
        Assert.Equal(11, receipt.Profile.Level);
        Assert.Equal(initial.Profile.Coins + 20, receipt.Profile.Coins);
        Assert.Equal(6, receipt.Rewards.Where(x => x.DecorationId is not null).Select(x => x.DecorationId).Distinct().Count());
        foreach (var reward in receipt.Rewards)
        {
            if (reward.DecorationId is null)
            {
                Assert.Null(reward.Rarity);
                Assert.Equal(5, reward.Compensation);
                continue;
            }
            Assert.Contains(reward.DecorationId, receipt.Profile.OwnedDecorationIds);
            var item = Assert.Single(RewardCatalog.Items, x => x.Id == reward.DecorationId);
            Assert.True(item.InCase);
            Assert.False(item.IsDefault);
            Assert.NotEqual("legendary", item.Rarity);
            Assert.Equal(reward.Rarity, item.Rarity);
            Assert.False(reward.Duplicate);
            Assert.Equal(0, reward.Compensation);
        }
        var again = await Claim(student.Client);
        Assert.Empty(again.Rewards);
        Assert.Equal(receipt.Profile.OwnedDecorationIds, again.Profile.OwnedDecorationIds);
        Assert.Equal(receipt.Profile.Coins, again.Profile.Coins);
        var rewardToEquip = receipt.Rewards.First(x => x.DecorationId is not null && RewardCatalog.Items.Single(i => i.Id == x.DecorationId).Type != "item");
        var response = await student.Client.PutAsJsonAsync($"/api/player/equipment/{rewardToEquip.DecorationId}", new { equipped = true });
        response.EnsureSuccessStatusCode();

        await SetState(factory, student.Id, 4400);
        var next = await Claim(student.Client);
        Assert.Equal(12, Assert.Single(next.Rewards).Level);
    }

    [Fact]
    public async Task ExhaustedRarityPaysFiveCoinsWithoutChangingXpKeysOrOtherPlayers()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        var other = await factory.Student(admin, "2");
        var initial = await Claim(student.Client);
        var owned = RewardCatalog.Items.Where(x => x.InCase && !x.IsDefault && x.Rarity == "common").Select(x => x.Id).ToArray();
        await SetState(factory, student.Id, 150, owned);

        var receipt = await Claim(student.Client);
        var reward = Assert.Single(receipt.Rewards);
        Assert.Equal(2, reward.Level);
        Assert.False(reward.Duplicate);
        Assert.Equal(5, reward.Compensation);
        Assert.Null(reward.DecorationId);
        Assert.Equal(initial.Profile.Coins + 5, receipt.Profile.Coins);
        Assert.Equal(150, receipt.Profile.Xp);
        Assert.Equal(initial.Profile.CaseKeys, receipt.Profile.CaseKeys);
        Assert.Equal(owned.Order(), receipt.Profile.OwnedDecorationIds.Order());
        var again = await Claim(student.Client);
        Assert.Empty(again.Rewards);
        Assert.Equal(receipt.Profile.Coins, again.Profile.Coins);
        var untouched = await Claim(other.Client);
        Assert.Empty(untouched.Rewards);
        Assert.Equal(1, untouched.Profile.Level);
        Assert.DoesNotContain(owned[0], untouched.Profile.OwnedDecorationIds);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/player/level-rewards/claim", new { })).StatusCode);
    }

    private static Task<PlayerLevelRewardReceipt> Claim(HttpClient client) =>
        Post<PlayerLevelRewardReceipt>(client, "/api/player/level-rewards/claim", new { });

    private static async Task SetState(CoreFactory factory, Guid actor, int xp, string[]? owned = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var state = await db.PlayerStates.SingleAsync(x => x.ParticipantId == actor);
        state.Xp = xp;
        if (owned is not null) state.OwnedJson = JsonSerializer.Serialize(owned);
        await db.SaveChangesAsync();
    }
}
