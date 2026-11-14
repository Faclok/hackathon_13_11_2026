using System.Security.Cryptography;

namespace BashkirSchool.Infrastructure;

public sealed record RewardItem(string Id, string Type, string Rarity, bool IsDefault = false, bool InCase = true);
public sealed record RewardCase(string Id, string[] Types);
public sealed record RewardDrop(string DecorationId, string Rarity, decimal ChancePercent);
public sealed record RewardCaseInfo(string Id, string[] Types, RewardDrop[] Drops);

public static class RewardCatalog
{
    public const int KeyPrice = 10;
    public const int DuplicateCompensation = 3;
    public static readonly RewardCase[] Cases =
    [
        new("case_hat", ["hat"]), new("case_frame", ["frame"]),
        new("case_bg", ["background"]), new("case_pet", ["pet", "item"])
    ];
    public static readonly RewardItem[] Items =
    [
        new("hat_none", "hat", "common", true), new("hat_tubeteika", "hat", "common"),
        new("hat_cap", "hat", "rare"), new("hat_burek", "hat", "epic"), new("hat_crown", "hat", "legendary"),
        new("hat_kurai_flower", "hat", "rare"), new("hat_festive_kashmau", "hat", "epic"),
        new("hat_akbuzat_wings", "hat", "legendary"),
        new("frame_none", "frame", "common", true), new("frame_neon_leaf", "frame", "common"),
        new("frame_fire_streak", "frame", "rare"), new("frame_kurai_gold", "frame", "epic"),
        new("frame_ural_gems", "frame", "legendary"),
        new("frame_bashkir_pattern", "frame", "rare"), new("frame_forest_honey", "frame", "epic"),
        new("frame_ural_radiance", "frame", "legendary"),
        new("bg_default", "background", "common", true), new("bg_honey_dawn", "background", "common"),
        new("bg_ural_mountains", "background", "rare"), new("bg_ufa_night", "background", "epic"),
        new("bg_golden_ornament", "background", "legendary"),
        new("pet_none", "pet", "common", true),
        new("prize_bg_kurai", "background", "rare"), new("prize_bg_yurt", "background", "common"),
        new("prize_bg_agidel", "background", "common"), new("prize_bg_shulgan_tash", "background", "epic"),
        new("prize_bg_honey", "background", "rare"), new("prize_bg_koshkar", "background", "legendary"),
        new("prize_pet_horse", "pet", "common"), new("prize_pet_akbuzat", "pet", "legendary"),
        new("prize_pet_bee", "pet", "rare"), new("prize_pet_eagle", "pet", "epic"),
        new("prize_pet_bear", "pet", "common"), new("prize_pet_wolf", "pet", "rare"),
        new("prize_pet_fox", "pet", "rare"), new("prize_pet_falcon", "pet", "epic"),
        new("prize_pet_kurai_spirit", "pet", "legendary"),
        new("prize_item_tubetey", "item", "common"), new("prize_item_kurai", "item", "rare"),
        new("prize_item_kubyz", "item", "rare"), new("prize_item_saba", "item", "epic"),
        new("prize_item_ichigi", "item", "common"), new("prize_item_chak_chak", "item", "rare"),
        new("prize_item_kumys", "item", "rare"), new("prize_item_carpet", "item", "epic"),
        new("prize_item_ural_heart", "item", "legendary"),
        new("mint", "background", "common", InCase: false), new("sky", "background", "common", InCase: false),
        new("lavender", "background", "common", InCase: false), new("ornament-emerald", "background", "common", InCase: false),
        new("ornament-sun", "background", "common", InCase: false), new("steppe", "background", "common", InCase: false),
        new("yurt", "background", "common", InCase: false), new("mountain", "background", "common", InCase: false),
        new("ornament", "background", "common", InCase: false), new("green", "frame", "common", InCase: false),
        new("gold", "frame", "common", InCase: false), new("flower", "frame", "common", InCase: false),
        new("blue", "frame", "common", InCase: false), new("berry", "frame", "common", InCase: false),
        new("diamond", "frame", "common", InCase: false), new("honey", "item", "common", InCase: false),
        new("kurai", "item", "common", InCase: false), new("hat", "item", "common", InCase: false),
        new("kubyz", "item", "common", InCase: false),
        new("pet_bee", "pet", "common", InCase: false), new("pet_marten", "pet", "rare", InCase: false),
        new("pet_bear", "pet", "epic", InCase: false), new("pet_eagle", "pet", "legendary", InCase: false)
    ];
    private static readonly (string Rarity, int Weight)[] Weights =
        [("common", 45), ("rare", 30), ("epic", 18), ("legendary", 7)];

    public static RewardItem[] Drops(RewardCase rewardCase) => Items.Where(x =>
        x.InCase && !x.IsDefault && rewardCase.Types.Contains(x.Type)).ToArray();

    public static RewardCaseInfo Describe(RewardCase rewardCase)
    {
        var pool = Drops(rewardCase);
        var tiers = Weights.Where(t => pool.Any(x => x.Rarity == t.Rarity)).ToArray();
        var total = tiers.Sum(x => x.Weight);
        return new(rewardCase.Id, rewardCase.Types, pool.Select(item =>
            new RewardDrop(item.Id, item.Rarity, 100m * tiers.Single(t => t.Rarity == item.Rarity).Weight /
                total / pool.Count(x => x.Rarity == item.Rarity))).ToArray());
    }

    public static RewardItem Roll(RewardCase rewardCase)
    {
        var pool = Drops(rewardCase);
        var tiers = Weights.Where(t => pool.Any(x => x.Rarity == t.Rarity)).ToArray();
        var point = RandomNumberGenerator.GetInt32(tiers.Sum(x => x.Weight));
        foreach (var tier in tiers)
        {
            point -= tier.Weight;
            if (point >= 0) continue;
            var items = pool.Where(x => x.Rarity == tier.Rarity).ToArray();
            return items[RandomNumberGenerator.GetInt32(items.Length)];
        }
        throw new InvalidOperationException("A case has no drops.");
    }
}
