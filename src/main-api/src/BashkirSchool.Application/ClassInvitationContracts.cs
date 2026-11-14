using BashkirSchool.Domain;

namespace BashkirSchool.Application;

/// <summary>Adds a self-registered participant identified by their exact username.</summary>
public sealed record InviteStudentRequest(string Username);
/// <summary>The invited participant's decision about joining a class.</summary>
public sealed record RespondToClassInvitationRequest(bool Accept);
/// <summary>A class invitation visible only to its participant or the class teacher.</summary>
public sealed record ClassInvitationResponse(Guid Id, Guid ClassId, string ClassName, Guid SchoolId,
    string SchoolName, Guid TeacherId, string TeacherUsername, Guid ParticipantId, string ParticipantUsername,
    ClassInvitationStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? RespondedAt)
{
    public string? TeacherDisplayName { get; init; }
    public string? ParticipantDisplayName { get; init; }
}

public interface IClassInvitations
{
    Task<ClassInvitationResponse> InviteAsync(Guid actor, Guid classId, InviteStudentRequest request, CancellationToken ct);
    Task<IReadOnlyList<ClassInvitationResponse>> ForStudentAsync(Guid actor, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<ClassInvitationResponse>> ForClassAsync(Guid actor, Guid classId, int offset, int limit, CancellationToken ct);
    Task<ClassInvitationResponse> RespondAsync(Guid actor, Guid invitationId, RespondToClassInvitationRequest request, CancellationToken ct);
}
