namespace BashkirSchool.Domain;

public sealed record ReviewObservation(Guid ParticipantId, Guid VariantId, Guid? SchoolId, Guid? ClassId,
    DateTimeOffset At, Guid ResponseId, Guid ShownVariantId)
{
    public IReadOnlyList<Guid>? ShownVariantIds { get; init; }
}
public sealed record CandidateSupport(Guid VariantId, int Supports, int IndependentObservations, decimal? Ratio, bool CoverageSatisfied, bool Qualified);

public static class ConsensusCalculator
{
    public static IReadOnlyList<CandidateSupport> Calculate(IEnumerable<ReviewObservation> observations,
        IEnumerable<TranslationVariant> variants, DistributionPolicy policy)
    {
        policy.Validate();
        var all = observations.ToArray();
        var latest = all.GroupBy(x => x.ParticipantId).Select(g => g.OrderByDescending(x => x.At).ThenByDescending(x => x.ResponseId).First()).ToArray();
        return variants.Select(v =>
        {
            var support = latest.Where(x => x.VariantId == v.Id).ToArray();
            var independent = all.Where(x => (x.ShownVariantIds?.Contains(v.Id) ?? (x.ShownVariantId == v.Id)) && x.ParticipantId != v.CreatedBy).Select(x => x.ParticipantId).Distinct().Count();
            decimal? ratio = latest.Length == 0 ? null : (decimal)support.Length / latest.Length;
            var schoolSupport = support.Where(x => x.SchoolId.HasValue && x.ClassId.HasValue).ToArray();
            var coverage = schoolSupport.Length > 0 && schoolSupport.Select(x => x.SchoolId).Distinct().Count() >= policy.MinimumSchools &&
                schoolSupport.Select(x => x.ClassId).Distinct().Count() >= policy.MinimumClasses &&
                schoolSupport.GroupBy(x => x.SchoolId).All(g => (decimal)g.Count() / schoolSupport.Length <= policy.MaximumSchoolShare) &&
                schoolSupport.GroupBy(x => x.ClassId).All(g => (decimal)g.Count() / schoolSupport.Length <= policy.MaximumClassShare);
            return new CandidateSupport(v.Id, support.Length, independent, ratio, coverage,
                independent >= policy.RequiredParticipants && coverage && ratio >= policy.SupportThreshold);
        }).ToArray();
    }
}
