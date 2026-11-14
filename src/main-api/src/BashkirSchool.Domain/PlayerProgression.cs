namespace BashkirSchool.Domain;

public sealed record PlayerProgress(int Level, int XpIntoLevel, int XpForNextLevel);

public static class PlayerProgression
{
    public const int XpPerQuestion = 5;
    public const int BossAnswerXp = 40;
    public const int ConfirmedAnswerXp = 20;
    public const int LevelCoins = 5;

    public static long LevelStartXp(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        var completed = (long)level - 1;
        return 25 * completed * (completed + 5);
    }

    public static PlayerProgress At(int xp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(xp);
        var low = 1;
        var high = 65536;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (LevelStartXp(mid) <= xp) low = mid;
            else high = mid - 1;
        }
        return new(low, (int)(xp - LevelStartXp(low)), 150 + 50 * (low - 1));
    }

    public static string? RewardRarity(int level) =>
        level % 10 == 0 ? "epic" : level % 5 == 0 ? "rare" : level % 2 == 0 ? "common" : null;
}
