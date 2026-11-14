using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class PlayerState
{
    public const int RegistrationBonusCoins = 40;

    public static PlayerState ForNewRegistration(Guid participantId) => new()
    {
        ParticipantId = participantId,
        Coins = RegistrationBonusCoins,
        OwnedJson = JsonSerializer.Serialize(RewardCatalog.Items.Where(x => x.IsDefault).Select(x => x.Id)),
        BackgroundId = "bg_default"
    };

    public Guid ParticipantId { get; set; }
    public int Xp { get; set; }
    public int LastRewardedLevel { get; set; } = 1;
    public int Coins { get; set; }
    public int CaseKeys { get; set; }
    public int BossAnswers { get; set; }
    public bool BossRewarded { get; set; }
    public DateOnly? LastDailyRewardDay { get; set; }
    public string OwnedJson { get; set; } = "[]";
    public string EquippedItemsJson { get; set; } = "[]";
    public string? BackgroundId { get; set; }
    public string? FrameId { get; set; }
    public string? HatId { get; set; }
    public string? PetId { get; set; }
}

public sealed class PlayerCaseOpening
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public string CaseId { get; set; } = "";
    public string DecorationId { get; set; } = "";
    public bool Duplicate { get; set; }
    public int Compensation { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

public sealed record PlayerProfile(Guid Id, string Nick, int Level, int Xp, int Coins, int Streak,
    int TasksSolved, int Accuracy, string[] TrophyIds, string[] OwnedDecorationIds, string? BackgroundId,
    string? FrameId, string? HatId, string? PetId, int CaseKeys, string[] EquippedItemIds, string LangPair = "ba-ru")
{
    public string AvatarUrl { get; init; } = "";
}
public sealed record PlayerLeaderboardEntry(PlayerProfile User, int Value, int Rank);
public sealed record PlayerLevelReward(int Level, string DecorationId, string Rarity, bool Duplicate, int Compensation);
public sealed record PlayerLevelRewardReceipt(PlayerProfile Profile, PlayerLevelReward[] Rewards);

public sealed class PlayerEconomy(CoreDbContext db, BossAccess access, IPermissionChecker permissions,
    TimeProvider clock, IConfiguration configuration)
{
    private static string[] ReadIds(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static string WriteIds(IEnumerable<string> ids) => JsonSerializer.Serialize(ids.Distinct().ToArray());

    public async Task<PlayerState> StateAsync(Guid actor, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "profile.read", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
        var state = await db.Set<PlayerState>().SingleOrDefaultAsync(x => x.ParticipantId == actor, ct);
        if (state is not null) return state;
        var defaults = WriteIds(RewardCatalog.Items.Where(x => x.IsDefault).Select(x => x.Id));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PlayerStates" ("ParticipantId", "Xp", "Coins", "CaseKeys", "BossAnswers", "BossRewarded",
                "OwnedJson", "EquippedItemsJson", "BackgroundId")
            VALUES ({actor}, 0, 0, 0, 0, FALSE, {defaults}, '[]', 'bg_default')
            ON CONFLICT ("ParticipantId") DO NOTHING
            """, ct);
        return await db.Set<PlayerState>().SingleAsync(x => x.ParticipantId == actor, ct);
    }

    public async Task<PlayerProfile> ProfileAsync(Guid actor, CancellationToken ct)
    {
        var state = await StateAsync(actor, ct);
        var progress = await access.ReadAsync(actor, ct);
        var participant = await db.Participants.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        var nick = await db.Users.Where(x => x.Id == actor).Select(x => x.UserName).SingleAsync(ct) ?? "";
        var trophies = new List<string>();
        if (progress.TasksSolved >= 1) trophies.Add("first");
        if (progress.TasksSolved >= 10) trophies.Add("learner");
        if (progress.Streak >= 7) trophies.Add("week");
        if (state.BossRewarded) trophies.Add("boss");
        if (progress.TasksSolved >= 100) trophies.Add("epic");
        if (progress.Streak >= 30) trophies.Add("month");
        if (progress.TasksSolved >= 250) trophies.Add("master");
        var accuracy = participant.Total == 0 ? 0 : (int)Math.Round((decimal)participant.Correct / participant.Total * 100m);
        return new(actor, nick, state.Xp / 100 + 1, state.Xp, state.Coins, progress.Streak, progress.TasksSolved,
            accuracy, trophies.ToArray(), ReadIds(state.OwnedJson), state.BackgroundId, state.FrameId,
            state.HatId, state.PetId, state.CaseKeys, ReadIds(state.EquippedItemsJson));
    }

    public async Task RewardReviewAsync(Guid actor, bool boss, CancellationToken ct)
    {
        var state = await StateAsync(actor, ct);
        if (boss)
        {
            state.BossAnswers++;
            if (!state.BossRewarded && state.BossAnswers >= 3)
            {
                state.BossRewarded = true;
                state.Xp += 100;
                state.Coins += 50;
            }
        }
        else
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
            if (state.LastDailyRewardDay != day)
            {
                state.LastDailyRewardDay = day;
                state.Xp += 20;
                state.Coins += 10;
            }
        }
    }

    public async Task<PlayerProfile> BuyKeysAsync(Guid actor, int quantity, CancellationToken ct)
    {
        if (quantity is < 1 or > 100) throw new DomainException("Quantity must be 1..100.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var state = await StateAsync(actor, ct);
        if (state.Coins < quantity * RewardCatalog.KeyPrice) throw new ConflictException("Insufficient coins.");
        state.Coins -= quantity * RewardCatalog.KeyPrice;
        state.CaseKeys += quantity;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ProfileAsync(actor, ct);
    }

    public async Task<PlayerLevelRewardReceipt> ClaimLevelRewardsAsync(Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var state = await StateAsync(actor, ct);
        var owned = ReadIds(state.OwnedJson).ToHashSet();
        var rewards = new List<PlayerLevelReward>();
        var currentLevel = state.Xp / 100 + 1;
        for (var level = Math.Max(2, state.LastRewardedLevel + 1); level <= currentLevel; level++)
        {
            var rarity = level % 10 == 0 ? "epic" : level % 5 == 0 ? "rare" : "common";
            var pool = RewardCatalog.Items.Where(x => x.InCase && !x.IsDefault && x.Rarity == rarity).ToArray();
            var unowned = pool.Where(x => !owned.Contains(x.Id)).ToArray();
            var choices = unowned.Length > 0 ? unowned : pool;
            var winner = choices[System.Security.Cryptography.RandomNumberGenerator.GetInt32(choices.Length)];
            var duplicate = !owned.Add(winner.Id);
            var compensation = duplicate ? RewardCatalog.DuplicateCompensation : 0;
            state.Coins += compensation;
            state.LastRewardedLevel = level;
            rewards.Add(new(level, winner.Id, rarity, duplicate, compensation));
        }
        state.OwnedJson = WriteIds(owned);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(await ProfileAsync(actor, ct), rewards.ToArray());
    }

    public async Task<PlayerCaseOpening> OpenCaseAsync(Guid actor, string caseId, CancellationToken ct)
    {
        var rewardCase = RewardCatalog.Cases.SingleOrDefault(x => x.Id == caseId) ?? throw new MissingResourceException();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var state = await StateAsync(actor, ct);
        if (state.CaseKeys < 1) throw new ConflictException("No case keys available.");
        var winner = RewardCatalog.Roll(rewardCase);
        var owned = ReadIds(state.OwnedJson).ToHashSet();
        var duplicate = !owned.Add(winner.Id);
        state.CaseKeys--;
        state.OwnedJson = WriteIds(owned);
        if (duplicate) state.Coins += RewardCatalog.DuplicateCompensation;
        var opening = new PlayerCaseOpening { ParticipantId = actor, CaseId = caseId,
            DecorationId = winner.Id, Duplicate = duplicate,
            Compensation = duplicate ? RewardCatalog.DuplicateCompensation : 0, OpenedAt = clock.GetUtcNow() };
        db.Add(opening);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return opening;
    }

    public async Task<PlayerProfile> EquipAsync(Guid actor, string itemId, bool equipped, CancellationToken ct)
    {
        var item = RewardCatalog.Items.SingleOrDefault(x => x.Id == itemId) ?? throw new MissingResourceException();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await CaseWriteLock.Acquire(db, ct);
        var state = await StateAsync(actor, ct);
        if (!ReadIds(state.OwnedJson).Contains(itemId)) throw new AccessDeniedException();
        if (item.Type == "background")
        {
            if (!equipped) throw new DomainException("A background must remain selected.");
            state.BackgroundId = itemId;
        }
        else if (item.Type == "frame") state.FrameId = equipped && !item.IsDefault ? itemId : null;
        else if (item.Type == "hat") state.HatId = equipped && !item.IsDefault ? itemId : null;
        else if (item.Type == "pet") state.PetId = equipped && !item.IsDefault ? itemId : null;
        else
        {
            var items = ReadIds(state.EquippedItemsJson).ToHashSet();
            if (equipped) items.Add(itemId); else items.Remove(itemId);
            state.EquippedItemsJson = WriteIds(items);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ProfileAsync(actor, ct);
    }

    public async Task<PlayerCaseOpening[]> HistoryAsync(Guid actor, CancellationToken ct)
    {
        await StateAsync(actor, ct);
        return await db.Set<PlayerCaseOpening>().AsNoTracking().Where(x => x.ParticipantId == actor)
            .OrderByDescending(x => x.OpenedAt).ThenByDescending(x => x.Id).Take(50).ToArrayAsync(ct);
    }

    public async Task<PlayerLeaderboardEntry[]> LeaderboardAsync(Guid actor, string period, string metric, CancellationToken ct)
    {
        if (period is not ("global" or "monthly") || metric is not ("tasks" or "streak"))
            throw new DomainException("Use global/monthly and tasks/streak.");
        await StateAsync(actor, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Learning:TimeZone"] ?? "Europe/Moscow");
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var cutoff = TimeZoneInfo.ConvertTime(monthStart, TimeZoneInfo.Utc);
        var accounts = await db.Participants.AsNoTracking().Select(x => x.Id).ToArrayAsync(ct);
        var result = new List<(PlayerProfile Profile, int Value)>();
        foreach (var id in accounts)
        {
            var profile = await ProfileAsync(id, ct);
            var value = metric == "streak" ? period == "global" ? profile.Streak : Math.Min(profile.Streak, now.Day)
                : period == "global" ? profile.TasksSolved :
                    await db.Responses.CountAsync(x => x.ParticipantId == id && x.Action != ReviewAction.Skip && x.SubmittedAt >= cutoff, ct)
                    + await (from attempt in db.Set<LevelAnswer>()
                             join question in db.Set<LevelQuestion>() on attempt.QuestionId equals question.Id
                             join level in db.Set<LearningLevel>() on question.LevelId equals level.Id
                             where level.ParticipantId == id && attempt.Recognized && attempt.SubmittedAt >= cutoff select attempt.Id).CountAsync(ct);
            result.Add((profile, value));
        }
        return result.OrderByDescending(x => x.Value).ThenBy(x => x.Profile.Nick, StringComparer.Ordinal)
            .ThenBy(x => x.Profile.Id).Select((x, i) => new PlayerLeaderboardEntry(x.Profile, x.Value, i + 1)).ToArray();
    }
}
