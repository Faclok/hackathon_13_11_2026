using BashkirSchool.Domain;

namespace BashkirSchool.Tests;

public sealed class StatisticsTests
{
    [Fact]
    public void EightyFiveChoicesForAAndFifteenForBGiveEightyFivePercent()
    {
        var variant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var opinions = Enumerable.Range(0, 100).Select(i => new Opinion(Guid.NewGuid(), i < 85 ? variant : other,
            JudgmentKind.Support, DateTimeOffset.UtcNow, Guid.NewGuid()));
        var statistics = ReviewStatisticsCalculator.Calculate(opinions, [variant, other]);
        Assert.Equal(100, statistics.UniqueParticipants);
        Assert.Equal(new VariantStatistics(variant, 85, .85m), statistics.Variants[0]);
        Assert.Equal(new VariantStatistics(other, 15, .15m), statistics.Variants[1]);
    }

    [Fact]
    public void NoOpinionsHaveUndefinedSupport()
    {
        var variant = Guid.NewGuid();
        var statistics = ReviewStatisticsCalculator.Calculate([], [variant]);
        Assert.Equal(0, statistics.UniqueParticipants);
        Assert.Equal(new VariantStatistics(variant, 0, null), Assert.Single(statistics.Variants));
    }

    [Fact]
    public void LatestAcceptanceReplacesPriorChoiceAndLegacyRejectionsAreIgnored()
    {
        var person = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var statistics = ReviewStatisticsCalculator.Calculate([
            new(person, a, JudgmentKind.Support, now, Guid.NewGuid()),
            new(person, a, JudgmentKind.Reject, now.AddDays(1), Guid.NewGuid()),
            new(person, b, JudgmentKind.Support, now.AddDays(1), Guid.NewGuid())], [a, b]);
        Assert.Equal(1, statistics.UniqueParticipants);
        Assert.Equal(new VariantStatistics(a, 0, 0m), statistics.Variants[0]);
        Assert.Equal(new VariantStatistics(b, 1, 1m), statistics.Variants[1]);
    }

    [Fact]
    public void AssignmentRejectsOtherParticipantAndExactExpirationBoundary()
    {
        var person = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var assignment = ReviewAssignment.Issue(person, TranslationCase.Open(Guid.NewGuid(), Guid.NewGuid(), now),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, TimeSpan.FromMinutes(30));
        Assert.Throws<DomainException>(() => assignment.Complete(Guid.NewGuid(), now));
        Assert.False(assignment.Completed);
        Assert.Throws<DomainException>(() => assignment.Complete(person, now.AddMinutes(30)));
        Assert.False(assignment.Completed);
        assignment.Complete(person, now.AddMinutes(30).AddTicks(-1));
        Assert.True(assignment.Completed);
    }
}
