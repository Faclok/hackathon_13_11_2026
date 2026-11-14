namespace BashkirSchool.Application;

/// <summary>A school created by the administrator.</summary>
public sealed record CreateSchoolRequest(string Name);
/// <summary>An account created by the administrator: teacher, expert or producer.</summary>
public sealed record CreateStaffRequest(string Username, string Profile, Guid? SchoolId = null);
/// <summary>A class belonging to the authenticated teacher's school.</summary>
public sealed record CreateClassRequest(Guid SchoolId, string Name);
/// <summary>A student account in one of the teacher's classes.</summary>
public sealed record CreateStudentRequest(string Username);
/// <summary>Public identity, without trust, permission profiles or password hashes.</summary>
public sealed record AccountResponse(Guid Id, string Username)
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string DisplayName => FirstName is not null && LastName is not null ? FirstName + " " + LastName : Username;
}
/// <summary>Credentials used to obtain a short lived JWT.</summary>
public sealed record SignInRequest(string Username, string Password);
/// <summary>A short lived JWT access token.</summary>
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null, Guid? SessionId = null, DateTimeOffset? RefreshExpiresAt = null);
/// <summary>A rotating opaque credential, submitted explicitly rather than as an ambient browser cookie.</summary>
public sealed record RefreshSessionRequest(string RefreshToken);
/// <summary>One of the authenticated account's sessions, without secret credentials.</summary>
public sealed record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
/// <summary>An operation-specific password setup token; it is not an access token.</summary>
public sealed record PasswordSetupResponse(Guid AccountId, string Token, DateTimeOffset ExpiresAt);
/// <summary>Completes password setup using the protected token.</summary>
public sealed record CompletePasswordRequest(Guid AccountId, string Token, string Password);
/// <summary>Changes the authenticated account's password after checking its current password.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
