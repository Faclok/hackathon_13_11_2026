namespace BashkirSchool.Application;

/// <summary>Creates an ordinary participant without a school, email or privileged capability selection.</summary>
public sealed record RegisterAccountRequest(string Username, string Password);
/// <summary>The caller's public identity and actual enrollment, if any.</summary>
public sealed record MyProfileResponse(Guid Id, string Username, Guid? SchoolId, Guid? ClassId);
/// <summary>Records the administrator's external identity-check basis; never include passwords or tokens.</summary>
public sealed record RecoverIndependentAccountRequest(string VerificationNote);
