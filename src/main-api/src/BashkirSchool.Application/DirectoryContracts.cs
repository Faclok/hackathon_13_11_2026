namespace BashkirSchool.Application;

/// <summary>Public school information.</summary>
public sealed record SchoolResponse(Guid Id, string Name);
/// <summary>A class visible to its teacher or administrator.</summary>
public sealed record ClassResponse(Guid Id, Guid SchoolId, string Name);
/// <summary>Assigns one of the fixed staff capabilities; it cannot edit the permission catalog.</summary>
public sealed record AssignAccessRequest(string Profile, Guid? SchoolId = null, Guid? CaseId = null);
/// <summary>An assignment visible only to an administrator managing access.</summary>
public sealed record AccessAssignmentResponse(Guid Id, string Profile, BashkirSchool.Domain.AccessScope Scope, Guid? ResourceId);
