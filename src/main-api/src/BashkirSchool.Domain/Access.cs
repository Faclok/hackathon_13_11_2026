namespace BashkirSchool.Domain;

public enum AccessScope { Own, School, Class, Global, Case }
public sealed class AccessProfile
{
    public string Id { get; set; } = "";
}
public sealed class ProfilePermission
{
    public string ProfileId { get; set; } = "";
    public string Permission { get; set; } = "";
}
public sealed class AccessAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubjectId { get; set; }
    public string ProfileId { get; set; } = "";
    public AccessScope Scope { get; set; }
    public Guid? ResourceId { get; set; }
}
public sealed class School
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}
public sealed class StudyClass
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchoolId { get; set; }
    public Guid TeacherId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class Enrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public Guid ClassId { get; set; }
}
public sealed class Participant
{
    public Guid Id { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
    public decimal Trust => (Correct + 5m) / (Total + 10m);
}
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid TargetId { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset At { get; set; }
}
