using BashkirSchool.Domain;

namespace BashkirSchool.Tests;

public sealed class IndependentCoverageTests
{
    [Fact]
    public void IndependentChoicesContributeToPercentageButNeverToSchoolOrClassCount()
    {
        var a = TranslationVariant.Create(Guid.NewGuid(), "A", true); var b = TranslationVariant.Create(a.CaseId, "B", false);
        var now = DateTimeOffset.UtcNow;
        var votes = new[] { new ReviewObservation(Guid.NewGuid(), a.Id, null, null, now, Guid.NewGuid(), a.Id),
            new ReviewObservation(Guid.NewGuid(), b.Id, null, null, now, Guid.NewGuid(), a.Id) };
        var policy = new DistributionPolicy { RequiredParticipants = 2, MinimumSchools = 1, MinimumClasses = 1, MaximumSchoolShare = 1, MaximumClassShare = 1 };
        var result = ConsensusCalculator.Calculate(votes, [a, b], policy);
        Assert.Equal(.5m, result[0].Ratio); Assert.Equal(.5m, result[1].Ratio);
        Assert.Equal(2, result[0].IndependentObservations); Assert.False(result[0].CoverageSatisfied); Assert.False(result[0].Qualified);
    }

    [Fact]
    public void IndependentVotesCannotDiluteAnOverrepresentedSchool()
    {
        var variant = TranslationVariant.Create(Guid.NewGuid(), "A", true); var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid();
        var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var votes = Enumerable.Range(0, 30).Select(i => new ReviewObservation(Guid.NewGuid(), variant.Id,
            i < 8 ? s1 : i < 10 ? s2 : null, i < 8 ? c1 : i < 10 ? c2 : null, now, Guid.NewGuid(), variant.Id)).ToArray();
        var policy = new DistributionPolicy { RequiredParticipants = 20, MinimumSchools = 2, MinimumClasses = 2, MaximumSchoolShare = .6m, MaximumClassShare = .6m };
        var result = Assert.Single(ConsensusCalculator.Calculate(votes, [variant], policy));
        Assert.Equal(30, result.Supports); Assert.Equal(30, result.IndependentObservations); Assert.Equal(1m, result.Ratio);
        Assert.False(result.CoverageSatisfied); Assert.False(result.Qualified);
    }

    [Fact]
    public void GroupSnapshotIsEitherRealSchoolAndClassOrNeitherNeverAnEmptyGuid()
    {
        var now = DateTimeOffset.UtcNow; var c = TranslationCase.Open(Guid.NewGuid(), Guid.NewGuid(), now); var person = Guid.NewGuid();
        var assignment = ReviewAssignment.Issue(person, c, null, null, Guid.NewGuid(), now, TimeSpan.FromMinutes(30));
        Assert.Null(assignment.SchoolId); Assert.Null(assignment.ClassId); Assert.Equal(person, assignment.ParticipantId);
        Assert.Throws<DomainException>(() => ReviewAssignment.Issue(person, c, null, Guid.NewGuid(), Guid.NewGuid(), now, TimeSpan.FromMinutes(30)));
        Assert.Throws<DomainException>(() => ReviewAssignment.Issue(person, c, Guid.Empty, Guid.Empty, Guid.NewGuid(), now, TimeSpan.FromMinutes(30)));
    }
}
