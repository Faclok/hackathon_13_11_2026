using BashkirSchool.Domain;

namespace BashkirSchool.Tests;

public sealed class ConsensusTests
{
    [Fact]
    public void ExactNAndEightyPercentAreInclusiveAndInsufficientCandidateCannotInheritSample()
    {
        var a = TranslationVariant.Create(Guid.NewGuid(), "A", true);
        var b = TranslationVariant.Create(a.CaseId, "B", false, createdBy: Guid.NewGuid());
        var school = Guid.NewGuid(); var group = Guid.NewGuid();
        var votes = Enumerable.Range(0, 100).Select(i => new ReviewObservation(Guid.NewGuid(), i < 80 ? a.Id : b.Id,
            school, group, DateTimeOffset.UtcNow, Guid.NewGuid(), a.Id)).ToArray();
        var policy = Policy(100);
        var result = ConsensusCalculator.Calculate(votes, [a, b], policy);
        Assert.True(result[0].Qualified);
        Assert.Equal(.8m, result[0].Ratio);
        Assert.Equal(100, result[0].IndependentObservations);
        Assert.False(result[1].Qualified);
        Assert.Equal(0, result[1].IndependentObservations);
        policy.RequiredParticipants = 101;
        Assert.False(ConsensusCalculator.Calculate(votes, [a], policy)[0].Qualified);
    }

    [Fact]
    public void CoverageAndAuthorIndependenceAreRequiredEvenWithUnanimousChoices()
    {
        var author = Guid.NewGuid();
        var a = TranslationVariant.Create(Guid.NewGuid(), "A", false, createdBy: author);
        var school = Guid.NewGuid(); var group = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var votes = new[] { new ReviewObservation(author, a.Id, school, group, now, Guid.NewGuid(), a.Id),
            new ReviewObservation(Guid.NewGuid(), a.Id, school, group, now, Guid.NewGuid(), a.Id) };
        var policy = Policy(2);
        var result = ConsensusCalculator.Calculate(votes, [a], policy)[0];
        Assert.Equal(1m, result.Ratio);
        Assert.Equal(1, result.IndependentObservations);
        Assert.False(result.Qualified);
        policy.RequiredParticipants = 1; policy.MinimumSchools = 2; policy.MinimumClasses = 2;
        policy.RequiredParticipants = 2;
        Assert.False(ConsensusCalculator.Calculate(votes, [a], policy)[0].CoverageSatisfied);
    }

    [Fact]
    public void RepeatChoicesDoNotMultiplyPeopleAndSmallCorrectionDoesNotRemoveAcceptedMajority()
    {
        var a = TranslationVariant.Create(Guid.NewGuid(), "A", true); var b = TranslationVariant.Create(a.CaseId, "B", false);
        var now = DateTimeOffset.UtcNow; var school = Guid.NewGuid(); var group = Guid.NewGuid();
        var votes = Enumerable.Range(0, 100).Select(_ => new ReviewObservation(Guid.NewGuid(), a.Id, school, group, now, Guid.NewGuid(), a.Id)).ToList();
        votes.Add(new(Guid.NewGuid(), b.Id, school, group, now.AddDays(1), Guid.NewGuid(), a.Id));
        votes.Add(votes[0] with { At = now.AddDays(1), ResponseId = Guid.NewGuid() });
        var stats = ConsensusCalculator.Calculate(votes, [a, b], Policy(100));
        Assert.Equal(100, stats[0].Supports);
        Assert.Equal(100m / 101, stats[0].Ratio);
        Assert.True(stats[0].Qualified);
        Assert.False(stats[1].Qualified);
    }

    private static DistributionPolicy Policy(int n) => new() { RequiredParticipants = n, MinimumSchools = 1, MinimumClasses = 1,
        MaximumSchoolShare = 1, MaximumClassShare = 1 };
}
