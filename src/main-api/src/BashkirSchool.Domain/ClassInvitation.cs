namespace BashkirSchool.Domain;

public enum ClassInvitationStatus { Pending, Accepted, Declined, Cancelled }

public sealed class ClassInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassId { get; set; }
    public Guid TeacherId { get; set; }
    public Guid ParticipantId { get; set; }
    public ClassInvitationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
