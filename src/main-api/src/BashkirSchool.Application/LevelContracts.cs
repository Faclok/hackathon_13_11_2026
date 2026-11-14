using System.Text.Json;

namespace BashkirSchool.Application;

/// <summary>The participant's current level. Only its next unanswered question is exposed.</summary>
public sealed record LevelResponse(Guid Id, long Number, int QuestionCount, int CorrectCount, LevelQuestionResponse Question);
/// <summary>A question and the token for exactly one submission, without its private answer key.</summary>
public sealed record LevelQuestionResponse(Guid Id, Guid AttemptToken, string Kind, int SchemaVersion, JsonElement Prompt);
/// <summary>A typed answer to the current question. Repeating the same token and answer is idempotent.</summary>
public sealed record AnswerLevelRequest(Guid AttemptToken, JsonElement Answer);
/// <summary>Check one issued word pair without submitting or consuming the question attempt.</summary>
public sealed record CheckWordPairRequest(Guid AttemptToken, string LeftId, string RightId);
public sealed record CheckWordPairResponse(bool Correct);
/// <summary>The immutable outcome of one attempt; fetch the current level to continue.</summary>
public sealed record LevelAnswerResponse(Guid AttemptToken, bool Recognized, bool LevelCompleted, string? CorrectAnswer = null)
{
    public int CoinsAwarded { get; init; }
    public int XpAwarded { get; init; }
}
/// <summary>Progress totals, including optional boss opportunities accumulated every five completed levels.</summary>
public sealed record LevelProgressResponse(long CompletedLevels, long CompletedBosses, long AvailableBosses,
    long Attempts, long Mistakes);
/// <summary>Historical level totals; past question prompts and answer keys are not exposed.</summary>
public sealed record LevelStatisticsResponse(long Number, int QuestionCount, int CorrectCount, int Attempts,
    int Mistakes, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt)
{
    public int CoinsAwarded { get; init; }
    public int XpAwarded { get; init; }
}
/// <summary>A boss consists of three sequential translation reviews and can be deferred.</summary>
public sealed record LevelBossResponse(Guid Id, long Checkpoint, int QuestionCount, int CompletedCount,
    AssignmentResponse? Question);
/// <summary>A saved boss review and the resulting boss completion state.</summary>
public sealed record LevelBossAnswerResponse(Guid ResponseId, bool Archived, bool BossCompleted)
{
    public int CoinsAwarded { get; init; }
    public int XpAwarded { get; init; }
}
