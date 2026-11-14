namespace BashkirSchool.Domain;

public sealed class LearningLevel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public long Number { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class LevelQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LevelId { get; set; }
    public int Position { get; set; }
    public long QueueOrder { get; set; }
    public Guid AttemptToken { get; set; } = Guid.NewGuid();
    public Guid? CaseId { get; set; }
    public Guid? ResolutionId { get; set; }
    public string Kind { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string PromptJson { get; set; } = "{}";
    public string AnswerKeyJson { get; set; } = "{}";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class LevelAnswer
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerJson { get; set; } = "{}";
    public bool Recognized { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public bool LevelCompleted { get; set; }
}

public sealed class LevelBoss
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public long Checkpoint { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class LevelBossQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BossId { get; set; }
    public int Position { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid? ResponseId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
