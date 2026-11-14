/// <summary>Registration payload; the service validates lengths and normalizes the username.</summary>
public record Credentials(string? Username, string? Password, string? DisplayName);
/// <summary>Password login payload.</summary>
public record Login(string? Username, string? Password);
/// <summary>User answer: confirm the candidate or provide a correction.</summary>
public record Submission(string? Action, string? AnswerText, Guid? TaskId = null);
/// <summary>Name received from the Telegram bot on /start.</summary>
public record TelegramStart(string? Name);
