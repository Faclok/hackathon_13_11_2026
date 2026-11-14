namespace BashkirSchool.Application;

/// <summary>A single-use Telegram start payload, valid for ten minutes.</summary>
public sealed record TelegramLinkResponse(string Code, DateTimeOffset ExpiresAt)
{
    public string? Url { get; init; }
}
/// <summary>The one-time code supplied by the owner through a private Telegram chat.</summary>
public sealed record CompleteTelegramLinkRequest(string Code);
/// <summary>The linked student's profile with school and class names for display in chat.</summary>
public sealed record TelegramProfileResponse(Guid Id, string Username, Guid? SchoolId, Guid? ClassId, string? SchoolName, string? ClassName);
