namespace BashkirSchool.Domain;

public sealed class DistributionPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public int RequiredParticipants { get; set; } = 100;
    public decimal SupportThreshold { get; set; } = .8m;
    public int MinimumSchools { get; set; } = 2;
    public int MinimumClasses { get; set; } = 3;
    public decimal MaximumSchoolShare { get; set; } = .6m;
    public decimal MaximumClassShare { get; set; } = .4m;
    public int AssignmentMinutes { get; set; } = 30;
    public int RepeatHours { get; set; } = 24;
    public int WorkingSetSize { get; set; } = 20;
    public Guid ChangedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public void Validate()
    {
        if (RequiredParticipants < 1 || MinimumSchools < 1 || MinimumClasses < MinimumSchools ||
            RequiredParticipants < MinimumClasses || SupportThreshold is <= 0 or > 1 ||
            MaximumSchoolShare is <= 0 or > 1 || MaximumClassShare is <= 0 or > 1 ||
            AssignmentMinutes < 1 || RepeatHours < 1 || WorkingSetSize < 1 ||
            Math.Floor(RequiredParticipants * MaximumSchoolShare) < 1 || Math.Floor(RequiredParticipants * MaximumClassShare) < 1)
            throw new DomainException("Distribution settings are incompatible.");
    }
}
