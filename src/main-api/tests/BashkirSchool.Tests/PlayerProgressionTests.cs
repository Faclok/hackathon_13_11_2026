using BashkirSchool.Domain;

namespace BashkirSchool.Tests;

public sealed class PlayerProgressionTests
{
    [Theory]
    [InlineData(0, 1, 0, 150)]
    [InlineData(149, 1, 149, 150)]
    [InlineData(150, 2, 0, 200)]
    [InlineData(151, 2, 1, 200)]
    [InlineData(349, 2, 199, 200)]
    [InlineData(350, 3, 0, 250)]
    [InlineData(899, 4, 299, 300)]
    [InlineData(900, 5, 0, 350)]
    [InlineData(3149, 9, 549, 550)]
    [InlineData(3150, 10, 0, 600)]
    [InlineData(11400, 20, 0, 1100)]
    public void ThresholdsUseIncreasingCost(int xp, int level, int into, int required)
        => Assert.Equal(new PlayerProgress(level, into, required), PlayerProgression.At(xp));

    [Fact]
    public void MaximumXpRemainsWithinComputedLevelAndNegativeXpIsRejected()
    {
        var progress = PlayerProgression.At(int.MaxValue);
        Assert.InRange(progress.XpIntoLevel, 0, progress.XpForNextLevel - 1);
        Assert.True(PlayerProgression.LevelStartXp(progress.Level) <= int.MaxValue);
        Assert.True(PlayerProgression.LevelStartXp(progress.Level + 1) > int.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerProgression.At(-1));
    }
}
